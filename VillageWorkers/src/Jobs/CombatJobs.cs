using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VillageWorkers.Jobs
{
    public class HuntingJob : HarvestJob
    {
        public HuntingJob(WorkerAI w, TaskConfig t) : base(w, t) { }

        protected override bool NeedsTool => true;
        protected override string ToolName => "a weapon";
        protected override string Verb => "Hunting";
        protected override float MaxWorkTime => 75f;
        protected override float Reach => 12f;

        public static List<string> DefaultTargets =>
            Plugin.HuntTargets.Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        protected override bool IsTool(ItemDrop.ItemData i)
        {
            if (!i.IsWeapon() || i.m_shared.m_skillType == Skills.SkillType.Pickaxes) return false;
            bool ranged = i.m_shared.m_attack.m_attackType == Attack.AttackType.Projectile;
            return !ranged || W.HaveAmmo(i);
        }

        protected override float ToolScore(ItemDrop.ItemData i)
        {
            float dmg = i.GetDamage().GetTotalDamage();
            // A bow is far better against skittish deer.
            return i.m_shared.m_attack.m_attackType == Attack.AttackType.Projectile ? dmg * 1.5f : dmg;
        }

        private bool IsPrey(Character c)
        {
            if (c == null || c.IsDead() || c.IsPlayer() || c.IsTamed() || c.GetComponent<WorkerAI>() != null) return false;
            string name = Utils.GetPrefabName(c.gameObject);
            return T.Filter.Count > 0 ? T.Filter.Contains(name) : DefaultTargets.Contains(name);
        }

        protected override Target FindTarget(Vector3 from, float radius)
        {
            return Nearest(Character.GetAllCharacters(), from, radius, IsPrey, c => c.GetHoverName().ToLower(),
                c => c.GetLevel() > 1 ? 10f : 0f);
        }

        protected override Vector3 ApproachPoint(Target t) => t.C != null ? t.C.transform.position : t.Pos;

        protected override R Work(float dt)
        {
            Character prey = Cur.C as Character;
            if (prey == null || prey.IsDead())
            {
                OnKill(Cur.Name, Cur.Pos);
                return R.Done;
            }
            Cur.Pos = prey.transform.position;
            if (Vector3.Distance(Center, prey.transform.position) > MaxRadius + 30f) return R.Failed;
            // Weapon broke: give up on this one and let Search() repair or fetch a new one.
            if (BestToolInInventory() == null) return R.Failed;
            W.Equip(BestToolInInventory());
            W.Fight(dt, prey, false);
            return R.Running;
        }

        private string _preyPrefab;

        private void OnKill(string niceName, Vector3 pos)
        {
            W.Record(Type, "kill:" + (_preyPrefab ?? niceName), 1);
            W.LogEvent("hunted down " + niceName);
            W.RaiseSkill(Type, 2f);
            Collector.Begin(pos, 6f, 8f, 2f);
        }

        public override void Tick(float dt)
        {
            if (P == Phase.Approach || P == Phase.Work)
            {
                Character prey = Cur?.C as Character;
                if (prey != null) _preyPrefab = Utils.GetPrefabName(prey.gameObject);
                // Chase instead of walking to where the animal used to be.
                if (P == Phase.Approach && prey != null && !prey.IsDead())
                {
                    W.Equip(BestToolInInventory());
                    W.SetStatus("Hunting " + Cur.Name);
                    P = Phase.Work;
                    WorkStarted = Time.time;
                    Busy = true;
                }
            }
            base.Tick(dt);
        }
    }

    public class GuardingJob : Job
    {
        public GuardingJob(WorkerAI w, TaskConfig t) : base(w, t) { }

        private Character _enemy;
        private float _nextScan;

        protected override bool IsTool(ItemDrop.ItemData i) => i.IsWeapon() && i.m_shared.m_skillType != Skills.SkillType.Pickaxes;
        protected override float ToolScore(ItemDrop.ItemData i) => i.GetDamage().GetTotalDamage();
        public override ItemDrop.ItemData BestToolInInventory() => BestTool(IsTool);

        private float Range => HasArea ? T.Area.R : 35f;

        public override bool CanStart(out string reason)
        {
            if (BestToolInInventory() == null && !ToolAvailableInChests() && !ToolRepairable())
            {
                reason = "needs a weapon";
                W.Ask("guard_weapon", "How am I supposed to guard the village with my bare hands? I need a weapon.");
                return false;
            }
            reason = null;
            return true;
        }

        public override void Start()
        {
            base.Start();
            _enemy = null;
        }

        public override void Tick(float dt)
        {
            ItemDrop.ItemData weapon = BestToolInInventory();
            if (weapon == null && ToolRepairable())
            {
                Busy = false;
                Repair.Tick(W, IsTool);
                return;
            }
            if (weapon == null)
            {
                R r = ToolFetcher.Tick(W, AllChests, i => IsTool(i) && WorkerAI.Usable(i), ToolScore, "a weapon");
                if (r == R.Failed) Block("needs a weapon", 30f);
                return;
            }
            W.Equip(weapon);

            if (Time.time > _nextScan)
            {
                _nextScan = Time.time + 1f;
                _enemy = FindEnemy();
            }

            if (_enemy != null && !_enemy.IsDead())
            {
                Busy = true;
                W.SetStatus("Defending the village from " + _enemy.GetHoverName());
                W.Fight(dt, _enemy, true);
                return;
            }
            if (_enemy != null)
            {
                W.Record(Type, "kill:" + Utils.GetPrefabName(_enemy.gameObject), 1);
                W.LogEvent("slew " + _enemy.GetHoverName());
                W.RaiseSkill(Type, 3f);
                _enemy = null;
            }

            Busy = false;
            W.SetStatus("Keeping watch");
            W.Wander(dt, Center, Mathf.Clamp(Range * 0.4f, 4f, 20f));
        }

        private Character FindEnemy()
        {
            Character best = null;
            float bestD = float.MaxValue;
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == null || c.IsDead() || c.IsPlayer() || c.IsTamed()) continue;
                if (!(c.GetBaseAI() is MonsterAI)) continue;
                if (c.GetFaction() == Character.Faction.AnimalsVeg || c.GetFaction() == Character.Faction.Dverger) continue;
                if (!BaseAI.IsEnemy(W.Character, c) || !BaseAI.IsEnemy(c, W.Character)) continue;
                if (!InRange(c.transform.position, Range)) continue;
                float d = Vector3.Distance(W.transform.position, c.transform.position);
                if (d < bestD)
                {
                    bestD = d;
                    best = c;
                }
            }
            return best;
        }
    }
}
