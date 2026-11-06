using System.Linq;
using UnityEngine;

namespace VillageWorkers
{
    /// <summary>
    /// Lets the player point at things in the world for a villager: a bed, chests, or a work area: a ring like the
    /// ward's that is sized with the mouse wheel, or a box marked by two opposite corners.
    /// </summary>
    public static class WorldPicker
    {
        public enum Mode { None, Bed, Chests, Area, Box }

        public static Mode CurrentMode { get; private set; } = Mode.None;
        public static bool Active => CurrentMode != Mode.None;
        public static bool SwallowEscapeThisFrame => Time.frameCount <= _finishedFrame + 1;

        private static WorkerAI _worker;
        private static int _task = -1;   // -1 = worker-wide (default chests)
        private static float _radius = 20f;
        private static Vector3 _areaPos;
        private static GameObject _marker;
        private static CircleProjector _circle;

        // Box areas turn in the same steps as building pieces, so they line up with the buildings around them.
        public const float BoxTurnStep = 22.5f;
        public const float MinBoxSize = 2f, MaxBoxSize = 200f;
        private static float _yaw;
        private static Vector3? _corner;
        private static BoxOutline _outline;
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
                _radius = t?.Area != null && !t.Area.IsBox ? t.Area.R : 20f;
                CreateMarker();
            }
            if (mode == Mode.Box)
            {
                TaskConfig t = Task(worker.Config);
                // Keep the way the old box was turned, or start square to where the player looks.
                _yaw = t?.Area != null && t.Area.IsBox ? t.Area.A
                    : Mathf.Repeat(Mathf.Round(Player.m_localPlayer.m_lookYaw.eulerAngles.y / BoxTurnStep) * BoxTurnStep, 360f);
                _corner = null;
                _outline = BoxOutline.Create();
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
                case Mode.Box:
                    UpdateBox();
                    if (Time.frameCount > _startFrame + 1 && ZInput.GetButtonDown("Use") && Player.m_localPlayer.GetHoverObject() == null)
                    {
                        OnInteract(null);
                        return;
                    }
                    if (_corner == null)
                        UI.Hint.Show($"Work area for <color=orange>{who}</color>",
                            "Look at one corner and press [<color=yellow>E</color>]    [<color=yellow>Mouse wheel</color>] turn    [<color=yellow>Esc</color>] cancel");
                    else
                        UI.Hint.Show($"Work area for <color=orange>{who}</color>",
                            $"{BoxSize} m - look at the opposite corner and press [<color=yellow>E</color>]    [<color=yellow>Mouse wheel</color>] turn    [<color=yellow>Esc</color>] cancel");
                    break;
            }
        }

        /// <summary>Used instead of where the player looks, when set (by the tests, which can't aim the camera precisely).</summary>
        public static Vector3? AimOverride;

        /// <summary>Where the player is looking, on the ground or a building.</summary>
        private static Vector3 AimPoint()
        {
            if (AimOverride.HasValue) return AimOverride.Value;
            Camera cam = GameCamera.instance != null ? GameCamera.instance.GetComponent<Camera>() : null;
            Vector3 pos = Player.m_localPlayer.transform.position;
            if (cam != null && Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, 120f,
                    LayerMask.GetMask("terrain", "Default", "static_solid", "piece")))
                pos = hit.point;
            return pos;
        }

        private static void UpdateArea()
        {
            float wheel = ZInput.GetMouseScrollWheel();
            if (Mathf.Abs(wheel) > 0.01f) _radius = Mathf.Clamp(_radius + Mathf.Sign(wheel) * (_radius > 30f ? 5f : 2f), 4f, 100f);

            Vector3 pos = AimPoint();
            _areaPos = pos;
            if (_marker == null) CreateMarker();
            if (_marker != null)
            {
                _marker.transform.position = pos;
                _circle.m_radius = _radius;
                _circle.m_nrOfSegments = Mathf.Clamp(Mathf.RoundToInt(_radius * 2.5f), 16, 160);
            }
        }

        private static string BoxSize => _outline != null ? $"{_outline.Width:0} x {_outline.Length:0}" : "";

        private static void UpdateBox()
        {
            float wheel = ZInput.GetMouseScrollWheel();
            if (Mathf.Abs(wheel) > 0.01f) _yaw = Mathf.Repeat(_yaw + Mathf.Sign(wheel) * BoxTurnStep, 360f);
            if (_outline == null) _outline = BoxOutline.Create();
            if (_outline == null) return;

            Vector3 aim = AimPoint();
            if (_corner == null)
            {
                // Before the first corner: a small square where it would go.
                _outline.Center = aim + Quaternion.Euler(0f, _yaw, 0f) * new Vector3(MinBoxSize, 0f, MinBoxSize) * 0.5f;
                _outline.Width = _outline.Length = MinBoxSize;
            }
            else
            {
                BoxBetween(_corner.Value, aim, _yaw, out _outline.Center, out _outline.Width, out _outline.Length);
            }
            _outline.Yaw = _yaw;
        }

        /// <summary>The box with two opposite corners and its sides turned <paramref name="yaw"/> degrees, at least MinBoxSize across.</summary>
        public static void BoxBetween(Vector3 corner, Vector3 opposite, float yaw, out Vector3 center, out float width, out float length)
        {
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 d = Quaternion.Inverse(rot) * (opposite - corner);
            float x = Mathf.Clamp(Mathf.Abs(d.x), MinBoxSize, MaxBoxSize) * (d.x < 0f ? -1f : 1f);
            float z = Mathf.Clamp(Mathf.Abs(d.z), MinBoxSize, MaxBoxSize) * (d.z < 0f ? -1f : 1f);
            width = Mathf.Abs(x);
            length = Mathf.Abs(z);
            center = corner + rot * new Vector3(x, 0f, z) * 0.5f;
            center.y = (corner.y + opposite.y) * 0.5f;
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
                case Mode.Box:
                {
                    Vector3 aim = AimPoint();
                    if (_corner == null)
                    {
                        _corner = aim;
                        return;
                    }
                    TaskConfig t = Task(cfg);
                    if (t != null)
                    {
                        BoxBetween(_corner.Value, aim, _yaw, out Vector3 center, out float width, out float length);
                        t.Area = AreaDef.Box(center, width, length, _yaw);
                        _worker.SendConfig(cfg);
                        player.Message(MessageHud.MessageType.Center, $"Work area set ({t.Area.Describe()})");
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
            if (_outline != null) Object.Destroy(_outline.gameObject);
            _outline = null;
            _corner = null;
            UI.Hint.Hide();
            if (reopen && w != null) UI.VillageWindow.Open(w);
        }
    }
}
