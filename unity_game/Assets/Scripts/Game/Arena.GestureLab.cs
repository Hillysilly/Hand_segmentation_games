using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Repulsor
{
    /// <summary>
    /// Debug: runs every gesture detector at once and shows what it sees: finger readings, which shapes are
    /// on, and each gesture as it fires, with aim lines. For tuning thresholds with real hands. Also logs
    /// readings as [Gesture] lines, so they can be read from adb afterwards.
    /// </summary>
    public sealed partial class Arena
    {
        static readonly Vector3 LabOffset = new Vector3(-0.3f, -0.02f, 0.8f);
        const int EventLines = 8;
        const float LogEvery = 1f;

        bool gestureLab;
        RectTransform labPanel;
        Text labText;
        LineRenderer[] aimLines;
        LineRenderer floorRing;
        readonly Queue<string> labEvents = new Queue<string>();
        readonly StringBuilder lab = new StringBuilder();
        readonly XArmsDetector labX = new XArmsDetector();
        readonly PowerUpDetector labPower = new PowerUpDetector();
        readonly TwoHandChargeDetector labCharge = new TwoHandChargeDetector();
        readonly TriangleDetector labTriangle = new TriangleDetector();
        readonly BowDetector labBow = new BowDetector();
        readonly FingerGunDetector[] labGun = { new FingerGunDetector(), new FingerGunDetector() };
        readonly PunchDetector[] labPunch = { new PunchDetector(), new PunchDetector() };
        readonly SpiderSignDetector[] labWeb = { new SpiderSignDetector(), new SpiderSignDetector() };
        readonly ForceGrabDetector[] labGrab = { new ForceGrabDetector(), new ForceGrabDetector() };
        readonly SaberGripDetector[] labSaber = { new SaberGripDetector(), new SaberGripDetector() };
        readonly UnderhandThrowDetector[] labThrow = { new UnderhandThrowDetector(), new UnderhandThrowDetector() };
        float labClock;
        float nextLabLog;
        bool labPlaced;

        void CreateGestureLab()
        {
            labPanel = Ui.Canvas("Gesture Lab", transform, new Vector2(330f, 300f));
            Ui.Box(labPanel, Vector2.zero, new Vector2(330f, 300f), new Color(0f, 0f, 0f, 0.6f));
            labText = Ui.Label(labPanel, "", Vector2.zero, new Vector2(314f, 290f), 11, TextAnchor.UpperLeft);
            labText.font = Ui.Font;
            labPanel.gameObject.SetActive(false);

            Material line = Resources.Load<Material>("AimLine");
            aimLines = new LineRenderer[3];
            for (int i = 0; i < aimLines.Length; i++)
                aimLines[i] = CreateLine("Lab Aim " + i, line, 0.006f, 2);
            floorRing = CreateLine("Lab Floor Ring", line, 0.012f, 33);
            floorRing.loop = true;
        }

        LineRenderer CreateLine(string name, Material material, float width, int points)
        {
            var go = new GameObject(name, typeof(LineRenderer));
            go.transform.SetParent(transform, false);
            var line = go.GetComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.widthMultiplier = width;
            line.positionCount = points;
            line.useWorldSpace = true;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            return line;
        }

        void TickGestureLab(float dt)
        {
            labPanel.gameObject.SetActive(gestureLab);
            if (!gestureLab)
            {
                foreach (LineRenderer line in aimLines)
                    line.enabled = false;
                floorRing.enabled = false;
                labPlaced = false;
                return;
            }

            labClock += dt;
            HandPose left = input.Left;
            HandPose right = input.Right;
            foreach (LineRenderer line in aimLines)
                line.enabled = false;

            if (labX.Step(left, right, body, dt) && !labWasX)
                LabEvent("SHIELD X on");
            labWasX = labX.Held;
            if (labPower.Step(left, right, body, dt))
                LabEvent("POWER-UP pose");
            if (labCharge.Step(left, right, body, dt, out Vector3 ballDirection, out float ballPower))
                LabEvent($"ENERGY BALL fired, power {ballPower:0.00}, dir {Fmt(ballDirection)}");
            if (labTriangle.Step(left, right, body, dt, out Vector3 pillar))
                LabEvent($"FIRE PILLAR at {Fmt(pillar)}, {Vector3.Distance(Flat(head.position), Flat(pillar)):0.0} m away");
            if (labBow.Step(left, right, body, dt, out Vector3 arrowFrom, out Vector3 arrowDirection, out float draw))
                LabEvent($"ARROW loosed, draw {draw:0.00}");
            if (labBow.Formed)
                ShowLine(2, labBow.StringPosition, labBow.BowPosition + labBow.Direction * 3f);

            for (int i = 0; i < 2; i++)
            {
                HandPose hand = i == 0 ? left : right;
                string side = i == 0 ? "L" : "R";
                if (labGun[i].Step(hand, body, dt, out _, out Vector3 shot))
                    LabEvent($"{side} FINGER GUN fired, dir {Fmt(shot)}");
                if (labGun[i].Aiming)
                    ShowLine(i, labGun[i].Origin, labGun[i].Origin + labGun[i].Direction * 4f);
                if (labPunch[i].Step(hand, body, dt, out _, out _))
                    LabEvent($"{side} PUNCH {hand.Velocity.magnitude:0.0} m/s");
                if (labWeb[i].Step(hand, body, dt, out _, out _))
                    LabEvent($"{side} WEB shot");
                GrabEvent grab = labGrab[i].Step(hand, body, dt, out Vector3 thrown);
                if (grab != GrabEvent.None)
                    LabEvent($"{side} FORCE {grab}{(grab == GrabEvent.Throw ? $" {thrown.magnitude:0.0} m/s" : "")}");
                if (labGrab[i].Holding || labGrab[i].Forming)
                    ShowLine(i, labGrab[i].AimOrigin, labGrab[i].AimOrigin + labGrab[i].AimDirection * 3f);
                bool wasSaber = labSaber[i].On;
                if (labSaber[i].Step(hand, dt) && !wasSaber)
                    LabEvent($"{side} SABER on");
                if (labSaber[i].On)
                    ShowLine(i, labSaber[i].Hilt, labSaber[i].Hilt + labSaber[i].BladeDirection * 1f);
                if (labThrow[i].Step(hand, body, dt, out _, out Vector3 bomb))
                    LabEvent($"{side} SMOKE BOMB thrown {bomb.magnitude:0.0} m/s");
            }

            floorRing.enabled = labTriangle.Aiming;
            if (labTriangle.Aiming)
            {
                for (int i = 0; i < 33; i++)
                {
                    float angle = i / 32f * Mathf.PI * 2f;
                    floorRing.SetPosition(i, labTriangle.Target + new Vector3(Mathf.Cos(angle), 0.02f, Mathf.Sin(angle)) * 0.5f);
                }
            }

            lab.Clear();
            lab.Append("<b>GESTURE LAB</b>\n");
            HandLine(lab, "L", left);
            HandLine(lab, "R", right);
            lab.Append($"X shield {(labX.Held ? "<color=#80FF80>ON</color>" : "off")}   ");
            lab.Append($"power-up {labPower.Progress * 100f:0}%   ");
            lab.Append($"pause {pauseGesture.Progress * 100f:0}%\n");
            lab.Append($"ball charge {labCharge.Charge * 100f:0}%   triangle {(labTriangle.Aiming ? "AIM" : "-")}   ");
            lab.Append($"bow {(labBow.Formed ? $"draw {labBow.Draw * 100f:0}%" : "-")}\n");
            if (left.Tracked && right.Tracked)
            {
                Vector3 l = body.Local(left.Wrist);
                Vector3 r = body.Local(right.Wrist);
                lab.Append($"wrists L {Fmt(l)} R {Fmt(r)} gap {Vector3.Distance(left.Wrist, right.Wrist):0.00}\n");
            }
            lab.Append("\n");
            foreach (string line in labEvents)
                lab.Append(line).Append('\n');
            labText.text = lab.ToString();

            Vector3 wanted = head.position + head.rotation * LabOffset;
            labPanel.position = labPlaced ? Vector3.Lerp(labPanel.position, wanted, 1f - Mathf.Exp(-4f * dt)) : wanted;
            labPanel.rotation = Quaternion.LookRotation(labPanel.position - head.position, head.up);
            labPlaced = true;

            if (labClock >= nextLabLog)
            {
                nextLabLog = labClock + LogEvery;
                Debug.Log($"[Gesture] {Reading("L", left)} | {Reading("R", right)} | X {labX.Held} power {labPower.Progress:0.00} pause {pauseGesture.Progress:0.00}");
            }
        }

        bool labWasX;

        void HandLine(StringBuilder text, string side, HandPose hand)
        {
            if (!hand.Tracked)
            {
                text.Append(side).Append(": <color=#FF8080>not tracked</color>\n");
                return;
            }
            text.Append(side).Append(": ").Append(Reading("", hand)).Append('\n');
            text.Append("   ");
            Shape(text, "fist", hand.Fist);
            Shape(text, "open", hand.Open);
            Shape(text, "ok", hand.Ok);
            Shape(text, "gun", hand.Gun);
            Shape(text, "spider", hand.SpiderSign);
            Shape(text, "cup", hand.Cup);
            text.Append($" speed {hand.Velocity.magnitude:0.0}\n");
        }

        static void Shape(StringBuilder text, string name, bool on) =>
            text.Append(on ? $"<color=#80FF80><b>{name.ToUpperInvariant()}</b></color> " : $"<color=#607080>{name}</color> ");

        static string Reading(string side, HandPose hand) => !hand.Tracked
            ? side + " -"
            : $"{side} i{hand.Index:0.00} m{hand.Middle:0.00} r{hand.Ring:0.00} l{hand.Little:0.00} thumb {hand.ThumbOut:0.00} pinch {hand.Pinch * 100f:0.0}cm";

        void ShowLine(int index, Vector3 from, Vector3 to)
        {
            LineRenderer line = aimLines[index];
            line.enabled = true;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
        }

        void LabEvent(string message)
        {
            string line = $"{labClock:0.0}s  {message}";
            labEvents.Enqueue(line);
            while (labEvents.Count > EventLines)
                labEvents.Dequeue();
            Debug.Log("[Gesture] " + message);
            game.PlayAt(head.position, game.ClickSound);
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
        static string Fmt(Vector3 v) => $"({v.x:0.00}, {v.y:0.00}, {v.z:0.00})";
    }
}
