using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VillageWorkers.Jobs;

namespace VillageWorkers
{
    public enum R
    {
        Running,
        Done,
        Failed
    }

    /// <summary>
    /// Brain of a villager. Runs on the ZDO owner only (like every Valheim AI); all persistent state lives
    /// in the ZDO so ownership can move between clients.
    /// </summary>
    public partial class WorkerAI : BaseAI, Interactable, TextReceiver
    {
        public static readonly List<WorkerAI> All = new List<WorkerAI>();

        public const string K_State = "vw_state";
        public const string K_Name = "vw_name";
        public const string K_Cfg = "vw_cfg";
        public const string K_Stats = "vw_stats";
        public const string K_Status = "vw_status";
        public const string K_Init = "vw_init";
        public const string K_Sleep = "vw_sleep";
        public const string K_Job = "vw_job";

        public Humanoid Human { get; private set; }
        public Container Box { get; private set; }
        public Inventory Inv => Human.GetInventory();
        public ZNetView NView => m_nview;
        public ZSyncAnimation Anim => m_animator;

        private WorkerConfig _cfg;
        private string _cfgJson;
        private WorkerStats _stats;
        private string _statsJson;
        private bool _statsDirty;
        private float _statsFlushTime;
        private bool _wasOwner;

        private string _status = "";
        private string _statusSent;
        private float _statusSendTime;

        private readonly List<Job> _jobs = new List<Job>();
        private string _jobsBuiltFor;
        private Job _job;
        private float _nextEval;
        private string _idleReason;

        private Character _threat;
        private float _nextThreatScan;
        private Character _lastAttacker;
        private float _lastAttackedTime = -999f;
        private float _fightUntil;

        private float _nextEquipCheck;
        private float _sleepWalkTime;
        private readonly Dictionary<string, float> _talkCooldown = new Dictionary<string, float>();
        private readonly Dictionary<int, float> _blacklist = new Dictionary<int, float>();

        // ------------------------------------------------------------------ lifecycle

        public override void Awake()
        {
            base.Awake();
            Human = GetComponent<Humanoid>();
            Box = GetComponent<Container>();
            All.Add(this);
            if (!m_nview.IsValid()) return;

            m_nview.Register<string>("vw_SetCfg", RPC_SetCfg);
            m_nview.Register<string>("vw_SetName", RPC_SetName);
            m_nview.Register<long>("vw_Recruit", RPC_Recruit);
            m_nview.Register("vw_Dismiss", RPC_Dismiss);
            m_nview.Register<ZPackage>("vw_Give", RPC_Give);
            m_nview.Register<string>("vw_Say", RPC_Say);
        }

        private void Start()
        {
            if (!m_nview.IsValid()) return;
            HookInventory();
            if (m_nview.IsOwner())
            {
                if (!m_nview.GetZDO().GetBool(K_Init)) FirstTimeSetup();
                OnBecameOwner();
            }
        }

        public new void OnDestroy()
        {
            base.OnDestroy();
            All.Remove(this);
            if (_statsDirty && m_nview != null && m_nview.IsValid() && m_nview.IsOwner()) FlushStats();
        }

        /// <summary>
        /// The worker's container shares the humanoid's inventory, so items given through the native
        /// container UI can be equipped directly.
        /// </summary>
        private void HookInventory()
        {
            if (Box == null || Box.m_inventory == Inv) return;
            Box.m_inventory = Inv;
            Inv.m_onChanged = (Action)Delegate.Combine(Inv.m_onChanged, new Action(Box.OnContainerChanged));
            Inv.m_name = GetName();
            ReloadBox();
        }

        /// <summary>Set while the villager deliberately reloads its inventory from the ZDO.</summary>
        public bool AllowBoxLoad;

        private void ReloadBox()
        {
            AllowBoxLoad = true;
            try
            {
                Box.m_lastRevision = uint.MaxValue;
                Box.Load();
            }
            finally
            {
                AllowBoxLoad = false;
            }
        }

        private void FirstTimeSetup()
        {
            ZDO zdo = m_nview.GetZDO();
            VisEquipment vis = Human.GetVisEquipment();
            bool female = UnityEngine.Random.value < 0.45f;
            vis.SetModel(female ? 1 : 0);
            zdo.Set(K_Name, Names.Random(female));

            float tone = UnityEngine.Random.Range(0f, 0.75f);
            vis.SetSkinColor(Vector3.Lerp(Vector3.one, new Vector3(0.45f, 0.33f, 0.24f), tone));
            Color hair = Color.Lerp(new Color(1f, 0.86f, 0.58f), new Color(0.55f, 0.32f, 0.16f), UnityEngine.Random.value)
                         * Mathf.Lerp(0.15f, 1f, UnityEngine.Random.value);
            vis.SetHairColor(new Vector3(hair.r, hair.g, hair.b));

            List<string> hairs = CustomizationItems("Hair");
            List<string> beards = CustomizationItems("Beard");
            if (hairs.Count > 0)
            {
                string h = hairs[UnityEngine.Random.Range(0, hairs.Count)];
                Human.SetHair(h);
                vis.SetHairItem(h.GetStableHashCode());
            }
            if (!female && beards.Count > 0 && UnityEngine.Random.value < 0.8f)
            {
                string b = beards[UnityEngine.Random.Range(0, beards.Count)];
                Human.SetBeard(b);
                vis.SetBeardItem(b.GetStableHashCode());
            }
            else
            {
                zdo.Set(ZDOVars.s_noBeard, true);
            }

            foreach (string cloth in new[] { "ArmorRagsChest", "ArmorRagsLegs" })
            {
                GameObject prefab = ObjectDB.instance?.GetItemPrefab(cloth);
                if (prefab == null) continue;
                ItemDrop.ItemData item = Human.PickupPrefab(prefab, 1, false);
                if (item != null) Human.EquipItem(item, false);
            }

            zdo.Set(K_State, (int)WorkerState.Wild);
            zdo.Set(K_Init, true);
            Inv.m_name = GetName();
        }

        private static List<string> CustomizationItems(string prefix)
        {
            List<string> list = new List<string>();
            if (ObjectDB.instance == null) return list;
            foreach (GameObject item in ObjectDB.instance.m_items)
            {
                if (item == null || !item.name.StartsWith(prefix)) continue;
                ItemDrop d = item.GetComponent<ItemDrop>();
                if (d != null && d.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Customization)
                    list.Add(item.name);
            }
            return list;
        }

        private void OnBecameOwner()
        {
            _stats = WorkerStats.FromJson(m_nview.GetZDO().GetString(K_Stats));
            _statsDirty = false;
            _job = null;
            _jobs.Clear();
            _jobsBuiltFor = null;
            _nextEval = 0f;
            _wasOwner = true;
            ReloadBox();
            EnsureEquipment();
        }

        // ------------------------------------------------------------------ accessors

        public WorkerState State => m_nview != null && m_nview.IsValid() ? (WorkerState)m_nview.GetZDO().GetInt(K_State) : WorkerState.Wild;

        public string GetName()
        {
            if (m_nview == null || !m_nview.IsValid()) return "Villager";
            string n = m_nview.GetZDO().GetString(K_Name);
            return string.IsNullOrEmpty(n) ? "Villager" : n;
        }

        public WorkerConfig Config
        {
            get
            {
                string json = m_nview.IsValid() ? m_nview.GetZDO().GetString(K_Cfg) : "";
                if (_cfg == null || json != _cfgJson)
                {
                    _cfg = WorkerConfig.FromJson(json);
                    _cfgJson = json;
                }
                return _cfg;
            }
        }

        public WorkerStats Stats
        {
            get
            {
                if (m_nview.IsValid() && m_nview.IsOwner() && _stats != null) return _stats;
                string json = m_nview.IsValid() ? m_nview.GetZDO().GetString(K_Stats) : "";
                if (_stats == null || json != _statsJson)
                {
                    _stats = WorkerStats.FromJson(json);
                    _statsJson = json;
                }
                return _stats;
            }
        }

        public string GetStatus()
        {
            if (m_nview.IsValid() && m_nview.IsOwner()) return _status;
            return m_nview.IsValid() ? m_nview.GetZDO().GetString(K_Status) : "";
        }

        public string CurrentJobTitle()
        {
            int j = m_nview.IsValid() ? m_nview.GetZDO().GetInt(K_Job, -1) : -1;
            return j < 0 ? "" : JobInfo.Profession((JobType)j);
        }

        public Vector3 Home
        {
            get
            {
                ObjRef bed = Config.Bed;
                if (bed != null && State == WorkerState.Villager) return bed.Pos;
                return m_spawnPoint != Vector3.zero ? m_spawnPoint : transform.position;
            }
        }

        public Character Character => m_character;
        public float SkillFactor(JobType job) => 1f + Stats.Skill(job) / 100f;
        public int Day => EnvMan.instance != null ? EnvMan.instance.GetDay() : 0;

        public override bool IsSleeping() => m_nview != null && m_nview.IsValid() && m_nview.GetZDO().GetBool(K_Sleep);

        // ------------------------------------------------------------------ owner-side API used by jobs and UI

        public void SetStatus(string s)
        {
            _status = s ?? "";
            if (!m_nview.IsOwner()) return;
            if (_status != _statusSent && Time.time > _statusSendTime)
            {
                m_nview.GetZDO().Set(K_Status, _status);
                _statusSent = _status;
                _statusSendTime = Time.time + 0.75f;
            }
        }

        public void Record(JobType job, string item, int amount)
        {
            if (!m_nview.IsOwner() || amount <= 0) return;
            Stats.Add(Day, job, item, amount);
            _statsDirty = true;
        }

        public void LogEvent(string msg)
        {
            if (!m_nview.IsOwner()) return;
            Stats.Log(ZNet.instance.GetTimeSeconds(), msg);
            _statsDirty = true;
        }

        public void RaiseSkill(JobType job, float amount)
        {
            if (!m_nview.IsOwner()) return;
            Stats.RaiseSkill(job, amount);
            _statsDirty = true;
        }

        private void FlushStats()
        {
            if (_stats == null) return;
            string json = _stats.ToJson();
            m_nview.GetZDO().Set(K_Stats, json);
            _statsJson = json;
            _statsDirty = false;
        }

        /// <summary>Says something in a chat bubble (seen by everybody nearby), at most once per cooldown per topic.</summary>
        public void Say(string text, string topic = null, float cooldown = 0f)
        {
            if (topic != null)
            {
                if (_talkCooldown.TryGetValue(topic, out float until) && Time.time < until) return;
                _talkCooldown[topic] = Time.time + cooldown;
            }
            m_nview.InvokeRPC(ZNetView.Everybody, "vw_Say", text);
        }

        /// <summary>Asks nearby players for something the worker can't do without (a tool, a chest...).</summary>
        public void Ask(string topic, string sentence)
        {
            if (Player.GetClosestPlayer(transform.position, 25f) == null) return;
            Say(sentence, "ask_" + topic, 75f);
        }

        public bool IsBlacklisted(Component c)
        {
            if (c == null) return true;
            int id = c.GetInstanceID();
            if (_blacklist.TryGetValue(id, out float until))
            {
                if (Time.time < until) return true;
                _blacklist.Remove(id);
            }
            return false;
        }

        public void Blacklist(Component c, float seconds = 120f)
        {
            if (c != null) _blacklist[c.GetInstanceID()] = Time.time + seconds;
        }

        // ------------------------------------------------------------------ main loop

        public override bool UpdateAI(float dt)
        {
            try
            {
                return UpdateAIInner(dt);
            }
            catch (Exception e)
            {
                // Never break the game's AI loop for every other creature.
                Plugin.Log.LogError($"{GetName()}: {e}");
                try { StopJob(); } catch { /* ignore */ }
                return true;
            }
        }

        private bool UpdateAIInner(float dt)
        {
            if (!base.UpdateAI(dt))
            {
                _wasOwner = false;
                return false;
            }
            if (!_wasOwner) OnBecameOwner();
            if (m_character.IsDead()) return true;

            if (_statsDirty && Time.time > _statsFlushTime)
            {
                _statsFlushTime = Time.time + 5f;
                FlushStats();
            }
            if (Time.time > _nextEquipCheck)
            {
                _nextEquipCheck = Time.time + 1f;
                EnsureEquipment();
                Inv.m_name = GetName();
            }

            WorkerState state = State;

            // Someone is looking into our pockets or managing us: stand still and pay attention.
            if (Box.IsInUse() || (state != WorkerState.Wild && UI.VillageWindow.IsManaging(this)))
            {
                StopMoving();
                Player p = Player.GetClosestPlayer(transform.position, 10f);
                if (p != null && !IsSleeping()) LookAt(p.GetEyePoint());
                return true;
            }

            if (HandleThreats(dt, state)) return true;

            switch (state)
            {
                case WorkerState.Wild:
                    Wake();
                    SetStatus("Wandering");
                    RandomMovement(dt, m_spawnPoint, true);
                    break;
                case WorkerState.Recruit:
                    Wake();
                    FollowPlayer(dt, Config.FollowPlayer, "Following you - assign me a bed");
                    break;
                case WorkerState.Villager:
                    VillagerUpdate(dt);
                    break;
            }
            return true;
        }

        private void VillagerUpdate(float dt)
        {
            WorkerConfig cfg = Config;
            if (cfg.FollowPlayer != 0)
            {
                Wake();
                StopJob();
                FollowPlayer(dt, cfg.FollowPlayer, "Following you");
                return;
            }

            if (IsSleepTime())
            {
                StopJob();
                SleepUpdate(dt);
                return;
            }

            Wake();
            WorkUpdate(dt);
        }

        public bool IsSleepTime()
        {
            if (EnvMan.instance == null) return false;
            float f = EnvMan.instance.GetDayFraction();
            return f < Plugin.WorkStart.Value || f > Plugin.WorkEnd.Value;
        }

        private void FollowPlayer(float dt, long playerId, string status)
        {
            Player p = Player.GetPlayer(playerId);
            if (p == null) p = Player.GetClosestPlayer(transform.position, 30f);
            if (p == null)
            {
                StopMoving();
                SetStatus(status);
                return;
            }
            SetStatus(status);
            Follow(p.gameObject, dt);
        }

        // ------------------------------------------------------------------ jobs

        private void WorkUpdate(float dt)
        {
            WorkerConfig cfg = Config;
            if (_jobsBuiltFor != _cfgJson) RebuildJobs(cfg);

            if (_jobs.Count == 0)
            {
                SetStatus("Waiting for orders");
                Ask("orders", "What should I do? Give me some work in the village menu.");
                IdleAtHome(dt);
                return;
            }

            if (_job == null || _job.Blocked || (Time.time > _nextEval && !_job.Busy)) EvaluateJobs();

            if (_job == null)
            {
                SetStatus("Idle: " + (_idleReason ?? "nothing to do"));
                IdleAtHome(dt);
                return;
            }

            Stats.WorkSec += dt;
            _job.Tick(dt);
        }

        private void RebuildJobs(WorkerConfig cfg)
        {
            StopJob();
            _jobs.Clear();
            for (int i = 0; i < cfg.Tasks.Count; i++)
            {
                TaskConfig t = cfg.Tasks[i];
                if (!t.Enabled) continue;
                Job job = Job.Create(this, t);
                if (job != null) _jobs.Add(job);
            }
            _jobsBuiltFor = _cfgJson;
            _nextEval = 0f;
        }

        private void EvaluateJobs()
        {
            _nextEval = Time.time + 20f;
            _idleReason = null;
            Job chosen = null;
            foreach (Job job in _jobs)
            {
                if (job == _job && !job.Blocked)
                {
                    chosen = job;
                    break;
                }
                if (Time.time < job.CooldownUntil)
                {
                    if (_idleReason == null) _idleReason = job.BlockReason;
                    continue;
                }
                string reason;
                bool ok;
                try { ok = job.CanStart(out reason); }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"{GetName()}: {job.Type} check failed: {e}");
                    ok = false;
                    reason = "confused";
                }
                if (ok)
                {
                    chosen = job;
                    break;
                }
                job.BlockReason = reason;
                job.CooldownUntil = Time.time + 12f;
                if (_idleReason == null) _idleReason = JobInfo.Title(job.Type) + ": " + reason;
            }

            if (chosen != _job)
            {
                _job?.Stop();
                _job = chosen;
                if (_job != null)
                {
                    _job.Start();
                    m_nview.GetZDO().Set(K_Job, (int)_job.Type);
                    Plugin.Debug($"{GetName()} starts {_job.Type}");
                }
                else
                {
                    m_nview.GetZDO().Set(K_Job, -1);
                }
            }
        }

        private void StopJob()
        {
            if (_job == null) return;
            _job.Stop();
            _job = null;
            m_nview.GetZDO().Set(K_Job, -1);
        }

        private float _idleDeliverCheck;
        private readonly Delivery _idleDelivery = new Delivery();

        private void IdleAtHome(float dt)
        {
            // Never stand around with full pockets.
            if (Time.time > _idleDeliverCheck && HasLoot())
            {
                R r = _idleDelivery.Tick(this, Config.Chests, null);
                if (r == R.Running) return;
                _idleDeliverCheck = Time.time + 30f;
            }
            RandomMovement(dt, Home, true);
        }

        // ------------------------------------------------------------------ sleeping

        private readonly Delivery _eveningDelivery = new Delivery();
        private float _eveningDeliveryGiveUp;

        private void SleepUpdate(float dt)
        {
            WorkerConfig cfg = Config;
            Bed bed = cfg.Bed?.Resolve<Bed>();
            if (bed == null)
            {
                Wake();
                if (cfg.Bed != null && Vector3.Distance(transform.position, cfg.Bed.Pos) > 4f)
                {
                    SetStatus("Going home");
                    MoveNear(cfg.Bed.Pos, 2f, false);
                }
                else
                {
                    SetStatus("I have no bed to sleep in!");
                    Ask("bed", "My bed is gone. Where am I supposed to sleep?");
                    StopMoving();
                }
                return;
            }

            if (IsSleeping())
            {
                SetStatus("Sleeping");
                PlaceInBed(bed);
                return;
            }

            // Drop off the day's loot first.
            if (HasLoot() && Time.time < _eveningDeliveryGiveUp)
            {
                R r = _eveningDelivery.Tick(this, cfg.Chests, null);
                if (r == R.Running) return;
            }

            Vector3 sp = bed.m_spawnPoint.position;
            float dist = Vector3.Distance(transform.position, sp);
            SetStatus("Going to bed");
            if (dist < 1.5f)
            {
                StartSleep(bed);
                return;
            }
            if (_sleepWalkTime <= 0f) _sleepWalkTime = Time.time;
            R m = MoveNear(sp, 1f, dist > 25f);
            // Beds are often behind doors or in tight corners; give the pathfinder a fair chance, then just lie down.
            if ((m == R.Failed && dist < 10f) || (Time.time - _sleepWalkTime > 45f && dist < 40f))
                StartSleep(bed);
        }

        private void StartSleep(Bed bed)
        {
            StopMoving();
            m_nview.GetZDO().Set(K_Sleep, true);
            m_animator.SetBool("attach_bed", true);
            Human.HideHandItems(false, false);
            PlaceInBed(bed);
            _sleepWalkTime = 0f;
        }

        private void PlaceInBed(Bed bed)
        {
            Transform sp = bed.m_spawnPoint;
            Vector3 pos = sp.position + sp.rotation * new Vector3(0f, 0.5f, 0f);
            transform.position = pos;
            transform.rotation = sp.rotation;
            m_body.position = pos;
            m_body.rotation = sp.rotation;
        }

        private void Wake()
        {
            _eveningDeliveryGiveUp = Time.time + 60f;
            _sleepWalkTime = 0f;
            if (!IsSleeping()) return;
            m_nview.GetZDO().Set(K_Sleep, false);
            m_animator.SetBool("attach_bed", false);
            Human.ShowHandItems(false, false);
            Bed bed = Config.Bed?.Resolve<Bed>();
            if (bed != null)
            {
                Vector3 p = bed.m_spawnPoint.position + bed.m_spawnPoint.forward * 0.8f + Vector3.up * 0.3f;
                transform.position = p;
                m_body.position = p;
            }
        }

        // ------------------------------------------------------------------ combat

        public override void OnDamaged(float damage, Character attacker)
        {
            base.OnDamaged(damage, attacker);
            if (attacker != null && attacker != m_character)
            {
                _lastAttacker = attacker;
                _lastAttackedTime = Time.time;
                _nextThreatScan = 0f;
            }
        }

        private bool HandleThreats(float dt, WorkerState state)
        {
            if (Time.time > _nextThreatScan)
            {
                _nextThreatScan = Time.time + 0.5f;
                _threat = FindThreat();
            }
            if (_threat == null || _threat.IsDead()) return false;

            Wake();
            bool guard = _job != null && _job.Type == JobType.Guarding;
            CombatMode mode = state == WorkerState.Villager ? Config.Combat : CombatMode.Flee;
            if (state == WorkerState.Recruit) mode = CombatMode.FightBack;
            if (guard) mode = CombatMode.FightBack;

            if (mode == CombatMode.Flee)
            {
                SetStatus("Fleeing from " + _threat.GetHoverName());
                Say("Help! Help!", "flee", 20f);
                Flee(dt, _threat.transform.position);
                return true;
            }

            SetStatus("Fighting " + _threat.GetHoverName());
            Fight(dt, _threat, true);
            return true;
        }

        private Character FindThreat()
        {
            Vector3 pos = transform.position;
            if (_lastAttacker != null && !_lastAttacker.IsDead() && Time.time - _lastAttackedTime < 12f
                && Vector3.Distance(pos, _lastAttacker.transform.position) < 35f && BaseAI.IsEnemy(m_character, _lastAttacker)
                && !_lastAttacker.IsPlayer())
                return _lastAttacker;

            Character best = null;
            float bestDist = 14f;
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == m_character || c.IsDead() || c.IsPlayer()) continue;
                BaseAI ai = c.GetBaseAI();
                if (ai == null || ai.GetTargetCreature() != m_character) continue;
                if (!BaseAI.IsEnemy(c, m_character)) continue;
                float d = Vector3.Distance(pos, c.transform.position);
                if (d < bestDist)
                {
                    best = c;
                    bestDist = d;
                }
            }
            return best;
        }

        private float _nextAttack;

        /// <summary>Moves into range and attacks the target with the best weapon at hand. Returns false when the target is gone.</summary>
        public bool Fight(float dt, Character target, bool preferWeapon)
        {
            if (target == null || target.IsDead()) return false;
            if (preferWeapon) EquipBestWeapon(target);

            ItemDrop.ItemData weapon = Human.GetCurrentWeapon();
            bool ranged = weapon != null && weapon.m_shared.m_attack.m_attackType == Attack.AttackType.Projectile;
            float range = ranged ? 22f : (weapon != null ? weapon.m_shared.m_attack.m_attackRange : 1.5f);
            Vector3 tp = target.transform.position;
            float dist = Vector3.Distance(transform.position, tp) - target.GetRadius();

            if (dist > range * 0.85f || (ranged && !CanSeeTarget(target)))
            {
                MoveTo(dt, tp, 0f, true);
                return true;
            }

            StopMoving();
            LookAt(target.GetCenterPoint());
            if (Time.time > _nextAttack && IsLookingAt(tp, ranged ? 8f : 25f))
            {
                if (Human.StartAttack(target, false)) _nextAttack = Time.time + (ranged ? 2.2f : 0.9f);
            }
            return true;
        }

        public void EquipBestWeapon(Character target)
        {
            ItemDrop.ItemData best = null;
            float bestScore = 0f;
            float dist = target != null ? Vector3.Distance(transform.position, target.transform.position) : 0f;
            foreach (ItemDrop.ItemData item in Inv.GetAllItems())
            {
                if (!item.IsWeapon() || !Usable(item)) continue;
                bool ranged = item.m_shared.m_attack.m_attackType == Attack.AttackType.Projectile;
                if (ranged && !HaveAmmo(item)) continue;
                float score = item.GetDamage().GetTotalDamage();
                if (ranged) score *= dist > 8f ? 1.6f : 0.3f;
                if (item.m_shared.m_skillType == Skills.SkillType.Pickaxes) score *= 0.5f;
                if (score > bestScore)
                {
                    best = item;
                    bestScore = score;
                }
            }
            if (best != null && !Human.IsItemEquiped(best)) Human.EquipItem(best, true);
        }

        public bool HaveAmmo(ItemDrop.ItemData weapon)
        {
            string ammo = weapon.m_shared.m_ammoType;
            if (string.IsNullOrEmpty(ammo)) return true;
            return Inv.GetAllItems().Any(i => i.m_shared.m_ammoType == ammo &&
                (i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo || i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.AmmoNonEquipable));
        }

        public static bool Usable(ItemDrop.ItemData item) => !item.m_shared.m_useDurability || item.m_durability > 0f;

        public override void OnDeath()
        {
            base.OnDeath();
            if (!m_nview.IsOwner()) return;
            Vector3 pos = transform.position;
            foreach (ItemDrop.ItemData item in Inv.GetAllItems().ToList())
            {
                Human.UnequipItem(item, false);
                ItemDrop.DropItem(item, 0, pos + Vector3.up * 0.5f + UnityEngine.Random.insideUnitSphere * 0.4f,
                    Quaternion.Euler(0, UnityEngine.Random.Range(0, 360), 0));
            }
            Inv.RemoveAll();
            if (State == WorkerState.Villager)
                MessageHud.instance?.MessageAll(MessageHud.MessageType.Center, $"{GetName()} has died!");
        }

        // ------------------------------------------------------------------ equipment

        private static readonly ItemDrop.ItemData.ItemType[] ArmorTypes =
        {
            ItemDrop.ItemData.ItemType.Helmet, ItemDrop.ItemData.ItemType.Chest, ItemDrop.ItemData.ItemType.Legs,
            ItemDrop.ItemData.ItemType.Shoulder, ItemDrop.ItemData.ItemType.Utility
        };

        public void EnsureEquipment()
        {
            Inventory inv = Inv;
            // Drop references to items that left the inventory without touching them (the player may own them now).
            bool stale = false;
            if (Human.m_rightItem != null && !inv.ContainsItem(Human.m_rightItem)) { Human.m_rightItem = null; stale = true; }
            if (Human.m_leftItem != null && !inv.ContainsItem(Human.m_leftItem)) { Human.m_leftItem = null; stale = true; }
            if (Human.m_chestItem != null && !inv.ContainsItem(Human.m_chestItem)) { Human.m_chestItem = null; stale = true; }
            if (Human.m_legItem != null && !inv.ContainsItem(Human.m_legItem)) { Human.m_legItem = null; stale = true; }
            if (Human.m_helmetItem != null && !inv.ContainsItem(Human.m_helmetItem)) { Human.m_helmetItem = null; stale = true; }
            if (Human.m_shoulderItem != null && !inv.ContainsItem(Human.m_shoulderItem)) { Human.m_shoulderItem = null; stale = true; }
            if (Human.m_utilityItem != null && !inv.ContainsItem(Human.m_utilityItem)) { Human.m_utilityItem = null; stale = true; }
            if (Human.m_ammoItem != null && !inv.ContainsItem(Human.m_ammoItem)) { Human.m_ammoItem = null; stale = true; }
            if (stale) Human.SetupEquipment();

            foreach (ItemDrop.ItemData item in inv.GetAllItems().ToList())
            {
                if (item.m_equipped && !Human.IsItemEquiped(item))
                {
                    item.m_equipped = false;
                    Human.EquipItem(item, false);
                }
            }

            // Wear the best armour available (but never items the worker crafted to deliver).
            string me = GetName();
            foreach (ItemDrop.ItemData.ItemType type in ArmorTypes)
            {
                ItemDrop.ItemData current = inv.GetAllItems().FirstOrDefault(i => i.m_equipped && i.m_shared.m_itemType == type);
                ItemDrop.ItemData best = inv.GetAllItems()
                    .Where(i => i.m_shared.m_itemType == type && i.m_crafterName != me && Usable(i))
                    .OrderByDescending(i => i.GetArmor())
                    .FirstOrDefault();
                if (best != null && best != current && (current == null || best.GetArmor() > current.GetArmor()))
                    Human.EquipItem(best, false);
            }
        }

        /// <summary>Equips the given item in the hands (if not already).</summary>
        public void Equip(ItemDrop.ItemData item)
        {
            if (item != null && !Human.IsItemEquiped(item)) Human.EquipItem(item, true);
        }

        public void UnequipHands()
        {
            if (Human.m_rightItem != null) Human.UnequipItem(Human.m_rightItem, false);
            if (Human.m_leftItem != null) Human.UnequipItem(Human.m_leftItem, false);
        }

        /// <summary>Items the worker keeps for itself: anything worn or held, plus the tools its jobs need.</summary>
        public HashSet<ItemDrop.ItemData> ReservedItems()
        {
            HashSet<ItemDrop.ItemData> set = new HashSet<ItemDrop.ItemData>();
            foreach (ItemDrop.ItemData i in Inv.GetAllItems())
                if (i.m_equipped) set.Add(i);
            foreach (Job job in _jobs)
            {
                ItemDrop.ItemData tool = job.BestToolInInventory();
                if (tool == null) continue;
                set.Add(tool);
                if (!string.IsNullOrEmpty(tool.m_shared.m_ammoType))
                    foreach (ItemDrop.ItemData a in Inv.GetAllItems())
                        if (a.m_shared.m_ammoType == tool.m_shared.m_ammoType && a != tool) set.Add(a);
            }
            return set;
        }

        public bool HasLoot()
        {
            HashSet<ItemDrop.ItemData> keep = ReservedItems();
            return Inv.GetAllItems().Any(i => !keep.Contains(i));
        }

        public int LootSlots()
        {
            HashSet<ItemDrop.ItemData> keep = ReservedItems();
            return Inv.GetAllItems().Count(i => !keep.Contains(i));
        }

        // ------------------------------------------------------------------ movement helpers

        private Vector3 _moveTarget = new Vector3(float.MaxValue, 0, 0);
        private float _moveBest;
        private float _moveProgressTime;
        private float _noPathTime;
        private float _doorCheck;
        private readonly List<KeyValuePair<Door, float>> _openedDoors = new List<KeyValuePair<Door, float>>();

        /// <summary>Walks towards a point. Done when within <paramref name="dist"/>, Failed when stuck or unreachable.</summary>
        public R MoveNear(Vector3 target, float dist, bool run)
        {
            float d = Vector3.Distance(transform.position, target);
            float dxz = Utils.DistanceXZ(transform.position, target);
            if (dxz <= dist && Mathf.Abs(transform.position.y - target.y) < 2.5f + dist)
            {
                StopMoving();
                ResetMove();
                return R.Done;
            }

            if ((target - _moveTarget).sqrMagnitude > 1f)
            {
                _moveTarget = target;
                _moveBest = d;
                _moveProgressTime = Time.time;
                _noPathTime = 0f;
            }
            if (d < _moveBest - 0.5f)
            {
                _moveBest = d;
                _moveProgressTime = Time.time;
            }

            OpenDoorsOnTheWay(target);

            bool arrived = MoveTo(Time.fixedDeltaTime, target, Mathf.Max(0.2f, dist * 0.8f), run || d > 30f);
            if (arrived)
            {
                // MoveTo also reports "arrived" when it can't find a path. Walk straight if it's close, else give up.
                if (_noPathTime <= 0f) _noPathTime = Time.time;
                if (dxz < 8f) MoveTowards(target - transform.position, false);
                if (Time.time - _noPathTime > 3f)
                {
                    ResetMove();
                    return R.Failed;
                }
            }
            else
            {
                _noPathTime = 0f;
            }

            if (Time.time - _moveProgressTime > 10f)
            {
                ResetMove();
                return R.Failed;
            }
            return R.Running;
        }

        private void ResetMove()
        {
            _moveTarget = new Vector3(float.MaxValue, 0, 0);
            _noPathTime = 0f;
        }

        private void OpenDoorsOnTheWay(Vector3 target)
        {
            if (Time.time < _doorCheck) return;
            _doorCheck = Time.time + 0.5f;

            for (int i = _openedDoors.Count - 1; i >= 0; i--)
            {
                Door door = _openedDoors[i].Key;
                if (door == null || !door.m_nview.IsValid())
                {
                    _openedDoors.RemoveAt(i);
                    continue;
                }
                if (Time.time > _openedDoors[i].Value && Vector3.Distance(door.transform.position, transform.position) > 3f)
                {
                    if (door.m_nview.GetZDO().GetInt(ZDOVars.s_state) != 0) door.m_nview.InvokeRPC("UseDoor", true);
                    _openedDoors.RemoveAt(i);
                }
            }

            Vector3 dir = (target - transform.position).Horizontal().normalized;
            foreach (Collider col in Physics.OverlapSphere(transform.position + Vector3.up, 2.2f, LayerMask.GetMask("piece", "piece_nonsolid", "door")))
            {
                Door door = col.GetComponentInParent<Door>();
                if (door == null || door.m_keyItem != null || !door.m_nview.IsValid()) continue;
                if (door.m_nview.GetZDO().GetInt(ZDOVars.s_state) != 0) continue;
                Vector3 to = (door.transform.position - transform.position).Horizontal();
                if (Vector3.Dot(to.normalized, dir) < 0.2f) continue;
                bool forward = Vector3.Dot(door.transform.forward, (transform.position - door.transform.position).normalized) < 0f;
                door.m_nview.InvokeRPC("UseDoor", forward);
                _openedDoors.Add(new KeyValuePair<Door, float>(door, Time.time + 4f));
            }
        }

        public void Stop() => StopMoving();
        public void Face(Vector3 point) => LookAt(point);
        public bool Facing(Vector3 point, float angle) => IsLookingAt(point, angle);
        public void Wander(float dt, Vector3 center, float range)
        {
            float old = m_randomMoveRange;
            m_randomMoveRange = range;
            RandomMovement(dt, center, true);
            m_randomMoveRange = old;
        }
        public void FleeFrom(float dt, Vector3 from) => Flee(dt, from);

        // ------------------------------------------------------------------ interaction

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !m_nview.IsValid() || user != Player.m_localPlayer) return false;
            if (alt)
            {
                if (State != WorkerState.Wild) TextInput.instance.RequestText(this, "$hud_rename", 20);
                return true;
            }

            switch (State)
            {
                case WorkerState.Wild:
                    m_nview.InvokeRPC("vw_Recruit", Player.m_localPlayer.GetPlayerID());
                    user.Message(MessageHud.MessageType.Center, $"{GetName()} will follow you. Assign a bed to make them a villager.");
                    return true;
                default:
                    UI.VillageWindow.Open(this);
                    return true;
            }
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            if (State == WorkerState.Wild || user != Player.m_localPlayer || item == null) return false;
            if (item.m_equipped) Player.m_localPlayer.UnequipItem(item, true);
            Inventory tmp = new Inventory("give", null, 1, 1);
            tmp.AddItem(item.Clone());
            ZPackage pkg = new ZPackage();
            tmp.Save(pkg);
            user.GetInventory().RemoveItem(item);
            m_nview.InvokeRPC("vw_Give", pkg);
            user.Message(MessageHud.MessageType.Center, $"Gave {Localization.instance.Localize(item.m_shared.m_name)} to {GetName()}");
            return true;
        }

        public string GetHoverText()
        {
            if (!m_nview.IsValid()) return "";
            string name = GetName();
            string text;
            switch (State)
            {
                case WorkerState.Wild:
                    text = $"{name} ( Wanderer )\n[<color=yellow><b>$KEY_Use</b></color>] Invite to your village";
                    break;
                case WorkerState.Recruit:
                    text = $"{name} ( Recruit )\n<color=#c0c0c0>{GetStatus()}</color>" +
                           "\n[<color=yellow><b>$KEY_Use</b></color>] Manage" +
                           "\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] $hud_rename";
                    break;
                default:
                    string job = CurrentJobTitle();
                    text = $"{name} ( {(string.IsNullOrEmpty(job) ? "Villager" : job)} )\n<color=#c0c0c0>{GetStatus()}</color>" +
                           "\n[<color=yellow><b>$KEY_Use</b></color>] Manage" +
                           "\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] $hud_rename" +
                           "\n[<color=yellow><b>1-8</b></color>] Give item";
                    break;
            }
            return Localization.instance.Localize(text);
        }

        public string GetText() => GetName();

        public void SetText(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length > 0) m_nview.InvokeRPC("vw_SetName", text);
        }

        // ------------------------------------------------------------------ remote commands (any client -> owner)

        public void SendConfig(WorkerConfig cfg) => m_nview.InvokeRPC("vw_SetCfg", cfg.ToJson());
        public void SendDismiss() => m_nview.InvokeRPC("vw_Dismiss");

        private void RPC_SetCfg(long sender, string json)
        {
            if (!m_nview.IsOwner()) return;
            WorkerConfig cfg = WorkerConfig.FromJson(json);
            WorkerState state = State;
            if (state == WorkerState.Recruit && cfg.Bed != null)
            {
                m_nview.GetZDO().Set(K_State, (int)WorkerState.Villager);
                cfg.FollowPlayer = 0;
                if (Stats.Hired < 0) Stats.Hired = Day;
                LogEvent("moved into the village");
                _statsDirty = true;
                Say("A bed of my own! I'm ready to work.");
            }
            m_nview.GetZDO().Set(K_Cfg, cfg.ToJson());
            _nextEval = 0f;
        }

        private void RPC_SetName(long sender, string name)
        {
            if (!m_nview.IsOwner()) return;
            string old = GetName();
            m_nview.GetZDO().Set(K_Name, name);
            Inv.m_name = name;
            LogEvent($"was renamed from {old} to {name}");
        }

        private void RPC_Recruit(long sender, long playerId)
        {
            if (!m_nview.IsOwner() || State != WorkerState.Wild) return;
            WorkerConfig cfg = Config.Clone();
            cfg.FollowPlayer = playerId;
            cfg.Employer = playerId;
            m_nview.GetZDO().Set(K_Cfg, cfg.ToJson());
            m_nview.GetZDO().Set(K_State, (int)WorkerState.Recruit);
            Say(Greetings[UnityEngine.Random.Range(0, Greetings.Length)]);
        }

        private static readonly string[] Greetings =
        {
            "A village? Lead the way!",
            "Finally, a place to call home. Show me where I can sleep.",
            "I'm a hard worker, you won't regret this!",
            "Skål! I'll follow you."
        };

        private void RPC_Dismiss(long sender)
        {
            if (!m_nview.IsOwner()) return;
            StopJob();
            Wake();
            HashSet<ItemDrop.ItemData> keep = new HashSet<ItemDrop.ItemData>(Inv.GetAllItems().Where(i => i.m_equipped &&
                (i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Chest || i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Legs)));
            foreach (ItemDrop.ItemData item in Inv.GetAllItems().ToList())
            {
                if (keep.Contains(item)) continue;
                Human.UnequipItem(item, false);
                ItemDrop.DropItem(item, 0, transform.position + transform.forward + Vector3.up * 0.5f, Quaternion.identity);
                Inv.RemoveItem(item);
            }
            m_nview.GetZDO().Set(K_State, (int)WorkerState.Wild);
            m_nview.GetZDO().Set(K_Cfg, new WorkerConfig().ToJson());
            m_nview.GetZDO().Set(K_Job, -1);
            m_spawnPoint = transform.position;
            m_nview.GetZDO().Set(ZDOVars.s_spawnPoint, m_spawnPoint);
            LogEvent("left the village");
            Say("Farewell then.");
        }

        private void RPC_Give(long sender, ZPackage pkg)
        {
            if (!m_nview.IsOwner()) return;
            Inventory tmp = new Inventory("give", null, 1, 1);
            tmp.Load(pkg);
            foreach (ItemDrop.ItemData item in tmp.GetAllItems().ToList())
            {
                ItemDrop.ItemData copy = item.Clone();
                copy.m_equipped = false;
                if (!Inv.CanAddItem(copy) || !Inv.AddItem(copy))
                {
                    ItemDrop.DropItem(item, 0, transform.position + Vector3.up, Quaternion.identity);
                    Say("My pockets are full, I'll drop it here.");
                }
                else
                {
                    Say("Thank you! That will come in handy.", "thanks", 10f);
                    LogEvent($"received {item.m_stack}x {Localization.instance.Localize(item.m_shared.m_name)}");
                }
            }
            _nextEval = 0f;
            foreach (Job j in _jobs) j.CooldownUntil = 0f;
        }

        private void RPC_Say(long sender, string text)
        {
            if (Chat.instance == null) return;
            Chat.instance.SetNpcText(gameObject, Vector3.up * 2.2f, 20f, 6f, "", text, false);
        }
    }
}
