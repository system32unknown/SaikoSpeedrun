using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

public class SpeedrunTimer : MonoBehaviour {
    // ---------------- Data ----------------
    [Serializable]
    public class Segment {
        public string name = "Segment";
        public double pbSplit = -1;        // cumulative time of personal best run
        public double bestSegment = -1;    // gold: best individual segment time
        public double split = -1;     // this run, cumulative
        public double segTime = -1;   // this run, individual
    }

    [Serializable]
    public class RunData {
        public string game = "My Game";
        public string category = "Any%";
        public int attempts;
        public int completed;
        public double pbTotal = -1;
        public List<Segment> segments = new List<Segment>();
    }

    enum State {
        Idle,
        Running,
        Paused,
        Finished
    }

    // ---------------- Settings ----------------
    [Header("Hotkeys")]
    public KeyCode toggleUIKey = KeyCode.F1;

    [Header("Window")]
    [Tooltip("Height is ignored: the window auto-sizes to its content.")]
    public Rect windowRect = new Rect(20, 20, 340, 0);
    public float maxListHeight = 220f;        // segment list scroll area (max)
    public float maxEditorListHeight = 160f;  // editor rows scroll area (max)
    public bool visible = true;
    public int fontSize = 14;

    [Header("Save")]
    public string saveFileName = "SpeedrunTimer.json";

    // ---------------- Runtime ----------------
    readonly RunData data = new RunData();
    State state = State.Idle;
    readonly Stopwatch sw = new Stopwatch();
    int current;                 // index of active segment
    bool editMode;
    Vector2 scroll;
    Vector2 editorScroll;
    string newSegName = "";

    // Styles
    GUIStyle sLabel, sRight, sTimer, sActive, sBtn;
    Texture2D texActive;
    int lastFontSize = -1;

    static readonly Color Green = new Color(0.30f, 0.90f, 0.40f);
    static readonly Color Red = new Color(0.95f, 0.35f, 0.35f);
    static readonly Color Gold = new Color(1.00f, 0.84f, 0.10f);
    static readonly Color Grey = new Color(0.75f, 0.75f, 0.75f);

    double Now => sw.Elapsed.TotalSeconds;

    // ---------------- Unity ----------------
    void Awake() {
        if (data.segments.Count == 0) {
            foreach (string n in new[] { "Segment 1", "Segment 2", "Segment 3" })
                data.segments.Add(new Segment { name = n });
        }
    }

    void OnGUI() {
        EnsureStyles();
        HandleHotkeys();
        if (!visible) return;

        // GUILayout.Window auto-sizes to its content; resetting height to 0 each
        // frame lets it shrink/grow when the editor is toggled.
        windowRect.height = 0;
        windowRect = GUILayout.Window(GetInstanceID(), windowRect, DrawWindow, $"{data.game} - {data.category}", GUILayout.Width(windowRect.width));
        windowRect.x = Mathf.Clamp(windowRect.x, 0, Mathf.Max(0, Screen.width - 50));
        windowRect.y = Mathf.Clamp(windowRect.y, 0, Mathf.Max(0, Screen.height - 30));
    }

    // ---------------- Input (via IMGUI events: works w/ any input backend) ----------------
    void HandleHotkeys() {
        Event e = Event.current;
        if (e.type != EventType.KeyDown || e.keyCode == KeyCode.None) return;
        if (editMode && GUIUtility.keyboardControl != 0) return; // typing in a text field

        if (e.keyCode == toggleUIKey) {
            visible = !visible;
            e.Use();
        }
    }

    // ---------------- Public API (callable from other mods / autosplitters) ----------------
    public void StartOrSplit() {
        if (data.segments.Count == 0) return;
        switch (state) {
            case State.Idle:
                ClearRun();
                data.attempts++;
                sw.Restart();
                state = State.Running;
                break;
            case State.Running:
                RecordSplit(false);
                break;
            case State.Paused:
                TogglePause();
                break;
            case State.Finished:
                break;
        }
    }

    public void SkipSplit() {
        if (state == State.Running && current < data.segments.Count - 1) RecordSplit(true);
    }

    public void UndoSplit() {
        if (state == State.Finished) {
            state = State.Running;
            sw.Start();
            data.completed--;
            current = data.segments.Count - 1;
            ClearSeg(current);
            return;
        }
        if ((state == State.Running || state == State.Paused) && current > 0) {
            current--;
            ClearSeg(current);
        }
    }

    public void TogglePause() {
        if (state == State.Running) {
            sw.Stop();
            state = State.Paused;
        } else if (state == State.Paused) {
            sw.Start();
            state = State.Running;
        }
    }

    public void ResetRun() {
        sw.Reset(); // Attempt abandoned; keep PB. Golds are only committed on finished runs.
        ClearRun();
        state = State.Idle;
    }

    // ---------------- Logic ----------------
    void ClearSeg(int i) {
        data.segments[i].split = -1;
        data.segments[i].segTime = -1;
    }

    void ClearRun() {
        current = 0;
        for (int i = 0; i < data.segments.Count; i++) ClearSeg(i);
    }

    void RecordSplit(bool skipped) {
        Segment seg = data.segments[current];
        if (skipped) {
            seg.split = -1;
            seg.segTime = -1;
        } else {
            double t = Now;
            seg.split = t;
            double prev = PreviousSplit(current);
            seg.segTime = t - prev;
        }

        if (current >= data.segments.Count - 1) FinishRun();
        else current++;
    }

    double PreviousSplit(int idx) {
        for (int i = idx - 1; i >= 0; i--)
            if (data.segments[i].split >= 0) return data.segments[i].split;
        return 0;
    }

    void FinishRun() {
        sw.Stop();
        state = State.Finished;
        data.completed++;

        Segment last = data.segments[data.segments.Count - 1];
        double total = last.split;

        // Gold segments
        foreach (Segment s in data.segments)
            if (s.segTime >= 0 && (s.bestSegment < 0 || s.segTime < s.bestSegment))
                s.bestSegment = s.segTime;

        // New PB
        if (!data.segments.Exists(s => s.split < 0) && (data.pbTotal < 0 || total < data.pbTotal)) {
            data.pbTotal = total;
            foreach (Segment s in data.segments) s.pbSplit = s.split;
        }
    }

    double SumOfBest() {
        double sum = 0;
        foreach (Segment s in data.segments) {
            if (s.bestSegment < 0) return -1;
            sum += s.bestSegment;
        }
        return sum;
    }

    // ---------------- GUI ----------------
    void DrawWindow(int id) {
        // Segment list
        float rowH = fontSize + 12f;
        float listH = Mathf.Min(data.segments.Count * rowH + 4f, maxListHeight);
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(listH));
        for (int i = 0; i < data.segments.Count; i++) DrawSegmentRow(i);
        GUILayout.EndScrollView();

        // Big timer
        double t = state == State.Idle ? 0 : Now;
        Color prevColor = GUI.contentColor;
        GUI.contentColor = state == State.Finished ? Gold : state == State.Paused ? Grey : Color.white;
        GUILayout.Label(Format(t, true), sTimer);
        GUI.contentColor = prevColor;

        // Info
        string pb = data.pbTotal >= 0 ? Format(data.pbTotal, false) : "--";
        double sob = SumOfBest();
        GUILayout.Label($"PB: {pb}   SoB: {(sob >= 0 ? Format(sob, false) : "--")}   " + $"Runs: {data.completed}/{data.attempts}", sLabel);

        // Buttons
        GUILayout.BeginHorizontal();
        string mainLabel = state == State.Idle ? "Start" : state == State.Running ? "Split" : state == State.Paused ? "Resume" : "Done";
        GUI.enabled = state != State.Finished;
        if (GUILayout.Button(mainLabel, sBtn)) StartOrSplit();
        if (GUILayout.Button("Skip", sBtn)) SkipSplit();
        GUI.enabled = true;
        if (GUILayout.Button("Undo", sBtn)) UndoSplit();
        GUI.enabled = state == State.Running || state == State.Paused;
        if (GUILayout.Button(state == State.Paused ? "Unpause" : "Pause", sBtn)) TogglePause();
        GUI.enabled = true;
        if (GUILayout.Button("Reset", sBtn)) ResetRun();
        GUILayout.EndHorizontal();

        // Editor toggle
        GUI.enabled = state == State.Idle || state == State.Finished;
        editMode = GUILayout.Toggle(editMode && GUI.enabled, "Edit splits", sLabel);
        GUI.enabled = true;
        if (editMode) DrawEditor();

        GUI.DragWindow(new Rect(0, 0, 10000, 22));
    }

    void DrawSegmentRow(int i) {
        Segment s = data.segments[i];
        bool isCurrent = (state == State.Running || state == State.Paused) && i == current;

        GUILayout.BeginHorizontal(isCurrent ? sActive : GUIStyle.none);
        GUILayout.Label(s.name, sLabel);

        // Delta + time columns
        string delta = "";
        Color dColor = Grey;
        string time;

        if (s.split >= 0) {
            time = Format(s.split, false);
            if (s.pbSplit >= 0) {
                double d = s.split - s.pbSplit;
                delta = FormatDelta(d);
                dColor = d <= 0 ? Green : Red;
                if (s.bestSegment >= 0 && s.segTime >= 0 && s.segTime <= s.bestSegment + 1e-9 && state == State.Finished) dColor = Gold;
            }
        } else if (isCurrent && s.pbSplit >= 0) {
            // Live delta only once we're behind the PB split (LiveSplit style)
            double d = Now - s.pbSplit;
            if (d > -5) { delta = FormatDelta(d); dColor = d <= 0 ? Green : Red; }
            time = Format(s.pbSplit, false);
        } else {
            time = s.pbSplit >= 0 ? Format(s.pbSplit, false) : "--";
        }

        Color old = GUI.contentColor;
        GUI.contentColor = dColor;
        GUILayout.Label(delta, sRight, GUILayout.Width(70));
        GUI.contentColor = s.split >= 0 ? Color.white : Grey;
        GUILayout.Label(time, sRight, GUILayout.Width(80));
        GUI.contentColor = old;
        GUILayout.EndHorizontal();
    }

    void DrawEditor() {
        GUILayout.Label("Game / Category", sLabel);
        GUILayout.BeginHorizontal();
        data.game = GUILayout.TextField(data.game);
        data.category = GUILayout.TextField(data.category);
        GUILayout.EndHorizontal();

        float editH = Mathf.Min(data.segments.Count * 26f + 4f, maxEditorListHeight);
        editorScroll = GUILayout.BeginScrollView(editorScroll, GUILayout.Height(editH));
        int removeIdx = -1, swapA = -1, swapB = -1;
        for (int i = 0; i < data.segments.Count; i++) {
            GUILayout.BeginHorizontal();
            data.segments[i].name = GUILayout.TextField(data.segments[i].name);
            if (GUILayout.Button("^", GUILayout.Width(26)) && i > 0) { swapA = i; swapB = i - 1; }
            if (GUILayout.Button("v", GUILayout.Width(26)) && i < data.segments.Count - 1) { swapA = i; swapB = i + 1; }
            if (GUILayout.Button("X", GUILayout.Width(26)) && data.segments.Count > 1) removeIdx = i;
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();

        // Apply structural changes after the layout pass so Layout/Repaint stay in sync.
        if (swapA >= 0) Swap(swapA, swapB);
        if (removeIdx >= 0) { data.segments.RemoveAt(removeIdx); ResetPbs(); }

        GUILayout.BeginHorizontal();
        newSegName = GUILayout.TextField(newSegName);
        if (GUILayout.Button("Add", GUILayout.Width(50)) && !string.IsNullOrEmpty(newSegName)) {
            data.segments.Add(new Segment { name = newSegName });
            newSegName = "";
            ResetPbs();
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Clear PB/Golds")) ResetPbs();
        GUILayout.EndHorizontal();
    }

    void Swap(int a, int b) {
        Segment t = data.segments[a];
        data.segments[a] = data.segments[b];
        data.segments[b] = t;
        ResetPbs();
    }

    // Changing segment layout invalidates comparisons
    void ResetPbs() {
        data.pbTotal = -1;
        foreach (Segment s in data.segments) { s.pbSplit = -1; s.bestSegment = -1; }
    }

    // ---------------- Formatting ----------------
    static string Format(double secs, bool big) {
        if (secs < 0) return "--";
        TimeSpan ts = TimeSpan.FromSeconds(secs);
        int cs = ts.Milliseconds / 10;
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}.{cs:00}";
        return $"{ts.Minutes}:{ts.Seconds:00}.{cs:00}";
    }

    static string FormatDelta(double d) {
        string sign = d < 0 ? "-" : "+";
        d = Math.Abs(d);
        TimeSpan ts = TimeSpan.FromSeconds(d);
        if (ts.TotalMinutes >= 1) return $"{sign}{(int)ts.TotalMinutes}:{ts.Seconds:00}.{ts.Milliseconds / 100}";
        return $"{sign}{ts.Seconds}.{ts.Milliseconds / 10:00}";
    }

    // ---------------- Styles ----------------
    void EnsureStyles() {
        if (sLabel != null && lastFontSize == fontSize) return;
        lastFontSize = fontSize;

        sLabel = new GUIStyle(GUI.skin.label) {
            fontSize = fontSize,
            wordWrap = false
        };
        sRight = new GUIStyle(sLabel) { alignment = TextAnchor.MiddleRight };
        sTimer = new GUIStyle(sLabel) {
            fontSize = fontSize * 3,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleRight
        };
        sBtn = new GUIStyle(GUI.skin.button) { fontSize = fontSize };

        if (texActive == null) {
            texActive = new Texture2D(1, 1);
            texActive.SetPixel(0, 0, new Color(0.2f, 0.35f, 0.7f, 0.5f));
            texActive.Apply();
        }
        sActive = new GUIStyle { normal = { background = texActive } };
    }
}