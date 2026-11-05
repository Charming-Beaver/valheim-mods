using System.Linq;
using UnityEngine;

namespace VillageWorkers
{
    /// <summary>
    /// Lets the player point at things in the world for a villager: a bed, chests, or a work area
    /// (a ring like the ward's that is sized with the mouse wheel).
    /// </summary>
    public static class WorldPicker
    {
        public enum Mode { None, Bed, Chests, Area }

        public static Mode CurrentMode { get; private set; } = Mode.None;
        public static bool Active => CurrentMode != Mode.None;
        public static bool SwallowEscapeThisFrame => Time.frameCount <= _finishedFrame + 1;

        private static WorkerAI _worker;
        private static int _task = -1;   // -1 = worker-wide (default chests)
        private static float _radius = 20f;
        private static Vector3 _areaPos;
        private static GameObject _marker;
        private static CircleProjector _circle;
        private static int _finishedFrame = -10;
        private static int _startFrame;
        private static int _added;

        public static void Begin(WorkerAI worker, Mode mode, int taskIndex = -1)
        {
            if (worker == null || Player.m_localPlayer == null) return;
            _worker = worker;
            CurrentMode = mode;
            _task = taskIndex;
            _startFrame = Time.frameCount;
            _added = 0;
            UI.VillageWindow.Close();

            if (mode == Mode.Area)
            {
                TaskConfig t = Task(worker.Config);
                _radius = t?.Area?.R ?? 20f;
                CreateMarker();
            }
        }

        private static TaskConfig Task(WorkerConfig cfg) => _task >= 0 && _task < cfg.Tasks.Count ? cfg.Tasks[_task] : null;

        public static void GlobalUpdate()
        {
            if (!Active)
            {
                UI.Hint.Hide();
                return;
            }
            if (_worker == null || Player.m_localPlayer == null)
            {
                Finish(false);
                return;
            }

            string who = _worker.GetName();
            switch (CurrentMode)
            {
                case Mode.Bed:
                    UI.Hint.Show($"Choose a bed for <color=orange>{who}</color>",
                        "Look at a bed and press [<color=yellow>E</color>]    [<color=yellow>Esc</color>] cancel");
                    break;
                case Mode.Chests:
                    string forWhat = _task >= 0 ? JobInfo.Title(_worker.Config.Tasks[_task].Type) : "everything";
                    UI.Hint.Show($"Chests for <color=orange>{who}</color> ({forWhat})",
                        $"Look at a chest and press [<color=yellow>E</color>] to add or remove it ({_added} changed)    [<color=yellow>Esc</color>] done");
                    break;
                case Mode.Area:
                    UpdateArea();
                    // Confirm even when looking at open ground (nothing hovered means Player.Interact isn't called).
                    if (Time.frameCount > _startFrame + 1 && ZInput.GetButtonDown("Use") && Player.m_localPlayer.GetHoverObject() == null)
                    {
                        OnInteract(null);
                        return;
                    }
                    UI.Hint.Show($"Work area for <color=orange>{who}</color>",
                        $"Radius {_radius:0} m - [<color=yellow>Mouse wheel</color>] resize    [<color=yellow>E</color>] confirm    [<color=yellow>Esc</color>] cancel");
                    break;
            }
        }

        private static void UpdateArea()
        {
            float wheel = ZInput.GetMouseScrollWheel();
            if (Mathf.Abs(wheel) > 0.01f) _radius = Mathf.Clamp(_radius + Mathf.Sign(wheel) * (_radius > 30f ? 5f : 2f), 4f, 100f);

            Camera cam = GameCamera.instance != null ? GameCamera.instance.GetComponent<Camera>() : null;
            Vector3 pos = Player.m_localPlayer.transform.position;
            if (cam != null && Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, 120f,
                    LayerMask.GetMask("terrain", "Default", "static_solid", "piece")))
                pos = hit.point;
            _areaPos = pos;
            if (_marker == null) CreateMarker();
            if (_marker != null)
            {
                _marker.transform.position = pos;
                _circle.m_radius = _radius;
                _circle.m_nrOfSegments = Mathf.Clamp(Mathf.RoundToInt(_radius * 2.5f), 16, 160);
            }
        }

        private static void CreateMarker()
        {
            if (_marker != null || ZNetScene.instance == null) return;
            GameObject ward = ZNetScene.instance.GetPrefab("guard_stone");
            PrivateArea pa = ward != null ? ward.GetComponent<PrivateArea>() : null;
            if (pa == null || pa.m_areaMarker == null) return;
            _marker = Object.Instantiate(pa.m_areaMarker.gameObject);
            _marker.SetActive(true);
            _circle = _marker.GetComponent<CircleProjector>();
        }

        public static void OnInteract(GameObject go)
        {
            if (!Active || Time.frameCount == _startFrame || _worker == null) return;
            WorkerConfig cfg = _worker.Config.Clone();
            Player player = Player.m_localPlayer;

            switch (CurrentMode)
            {
                case Mode.Bed:
                {
                    Bed bed = go != null ? go.GetComponentInParent<Bed>() : null;
                    if (bed == null)
                    {
                        player.Message(MessageHud.MessageType.Center, "That's not a bed.");
                        return;
                    }
                    WorkerAI other = WorkerAI.All.FirstOrDefault(w => w != _worker && w.State == WorkerState.Villager && w.Config.Bed != null && w.Config.Bed.Matches(bed.m_nview));
                    if (other != null)
                    {
                        player.Message(MessageHud.MessageType.Center, $"That bed already belongs to {other.GetName()}.");
                        return;
                    }
                    cfg.Bed = new ObjRef(bed.m_nview);
                    _worker.SendConfig(cfg);
                    player.Message(MessageHud.MessageType.Center, $"{_worker.GetName()} will sleep here.");
                    Finish(true);
                    return;
                }
                case Mode.Chests:
                {
                    Container c = go != null ? go.GetComponentInParent<Container>() : null;
                    if (c == null || c.GetComponent<Piece>() == null || c.GetComponent<WorkerAI>() != null)
                    {
                        player.Message(MessageHud.MessageType.Center, "That's not a chest.");
                        return;
                    }
                    ObjRef r = new ObjRef(c.m_nview);
                    var list = Task(cfg)?.Chests ?? cfg.Chests;
                    ObjRef existing = list.FirstOrDefault(x => x.Same(r));
                    if (existing != null)
                    {
                        list.Remove(existing);
                        player.Message(MessageHud.MessageType.Center, "Chest removed");
                    }
                    else
                    {
                        list.Add(r);
                        player.Message(MessageHud.MessageType.Center, "Chest added");
                    }
                    _added++;
                    _worker.SendConfig(cfg);
                    return;
                }
                case Mode.Area:
                {
                    TaskConfig t = Task(cfg);
                    if (t != null)
                    {
                        t.Area = new AreaDef(_areaPos, _radius);
                        _worker.SendConfig(cfg);
                        player.Message(MessageHud.MessageType.Center, $"Work area set ({_radius:0} m)");
                    }
                    Finish(true);
                    return;
                }
            }
        }

        public static void Finish(bool reopen)
        {
            WorkerAI w = _worker;
            CurrentMode = Mode.None;
            _worker = null;
            _finishedFrame = Time.frameCount;
            if (_marker != null) Object.Destroy(_marker);
            _marker = null;
            UI.Hint.Hide();
            if (reopen && w != null) UI.VillageWindow.Open(w);
        }
    }
}
