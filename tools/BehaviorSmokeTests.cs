using System;
using System.Reflection;
using System.Windows.Forms;

internal static class BehaviorSmokeTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Get(PetForm pet, string name) { return typeof(PetForm).GetField(name, Private).GetValue(pet); }
    private static void Set(PetForm pet, string name, object value) { typeof(PetForm).GetField(name, Private).SetValue(pet, value); }
    private static void Call(PetForm pet, string name, params object[] args) { typeof(PetForm).GetMethod(name, Private).Invoke(pet, args); }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void State(PetForm pet, string expected) { Check(Get(pet, "state").ToString() == expected, "Expected state " + expected); }

    [STAThread]
    private static void Main()
    {
        CodexStatus.DirectoryPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime", "ui-tests-" + Guid.NewGuid().ToString("N"));
        using (PetForm pet = new PetForm())
        {
            Call(pet, "ToggleWalking");
            Call(pet, "Feed");
            State(pet, "Expression");
            Call(pet, "ToggleSleep");
            State(pet, "Sleeping");
            Check(((string[])Get(pet, "animation")).Length == 0, "Sleep must cancel expression");
            var position = pet.Location;
            Set(pet, "nextWalk", DateTime.MinValue);
            Set(pet, "nextBlink", DateTime.MinValue);
            Call(pet, "MovementTick", null, EventArgs.Empty);
            Call(pet, "AnimationTick", null, EventArgs.Empty);
            State(pet, "Sleeping");
            Check(pet.Location == position, "Sleeping pet moved");
            Set(pet, "dragging", true);
            Call(pet, "MouseUpHandler", null, new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
            State(pet, "Sleeping");
            Check(!((Timer)Get(pet, "bounceTimer")).Enabled, "Sleeping drag triggered bounce");
            Call(pet, "DoubleClickHandler", null, new MouseEventArgs(MouseButtons.Left, 2, 0, 0, 0));
            State(pet, "Waking");
            Set(pet, "nextWalk", DateTime.MinValue);
            Call(pet, "MovementTick", null, EventArgs.Empty);
            State(pet, "Waking");
            FinishAnimation(pet);
            State(pet, "Idle");
            Check((bool)Get(pet, "walkingPaused"), "Wake lost walking preference");
            Call(pet, "ToggleWalking");
            Set(pet, "nextWalk", DateTime.MinValue);
            Call(pet, "MovementTick", null, EventArgs.Empty);
            State(pet, "Walking");
            Call(pet, "ToggleSleep");
            Check((int)Get(pet, "walkStepsRemaining") == 0, "Sleep did not cancel walk");
            Call(pet, "Feed");
            State(pet, "Expression");
            Check(!(bool)Get(pet, "sleeping"), "Feed did not wake pet");
            Call(pet, "StartBounce");
            State(pet, "Bouncing");
            Call(pet, "ToggleSleep");
            Check(!((Timer)Get(pet, "bounceTimer")).Enabled, "Sleep did not stop bounce");
            Call(pet, "WakeUp");
            FinishAnimation(pet);
            Set(pet, "facingDx", -1);
            Call(pet, "Happy");
            Call(pet, "AnimationTick", null, EventArgs.Empty);
            var picture = (PictureBox)Get(pet, "pet");
            var frames = (System.Collections.Generic.Dictionary<string, System.Drawing.Image>)Get(pet, "frames");
            Check(Object.ReferenceEquals(picture.Image, frames["happy_left"]), "Expression changed facing");
            Call(pet, "ToggleSleep");
            Check(Object.ReferenceEquals(picture.Image, frames["sleep_left"]), "Sleep changed facing");
            Set(pet, "sleepStarted", DateTime.Now.AddSeconds(-1.5));
            Call(pet, "AnimationTick", null, EventArgs.Empty);
            Check(Object.ReferenceEquals(picture.Image, frames["sleep_breathe_left"]), "Missing breathing frame");
            Check(picture.Top == 28, "Breathing moved ground baseline");
            frames["sleep"].Save("assets/sprites/sleep_preview.png");
            frames["wake"].Save("assets/sprites/wake_preview.png");
            Call(pet, "PatHead");
            State(pet, "Sleeping");
            Call(pet, "WakeUp");
            FinishAnimation(pet);
            for (int action = 0; action < 3; action++)
            {
                Call(pet, "StartIdleAction", action);
                State(pet, "Expression");
                var before = pet.Location;
                Call(pet, "MovementTick", null, EventArgs.Empty);
                Check(pet.Location == before, "Idle action moved pet");
                FinishAnimation(pet);
                State(pet, "Idle");
            }
            var headClick = new MouseEventArgs(MouseButtons.Left, 1, picture.Width / 2, picture.Height / 3, 0);
            Call(pet, "MouseDownHandler", picture, headClick);
            Call(pet, "MouseUpHandler", picture, headClick);
            Check((bool)Get(pet, "pendingPat"), "Head click not queued");
            Set(pet, "pendingPatAt", DateTime.MinValue);
            Call(pet, "AnimationTick", null, EventArgs.Empty);
            State(pet, "Expression");
            Check(!(bool)Get(pet, "pendingPat"), "Head click repeated");
            FinishAnimation(pet);
            Call(pet, "MouseDownHandler", picture, headClick);
            Call(pet, "MouseUpHandler", picture, headClick);
            Call(pet, "MouseDownHandler", picture, headClick);
            Call(pet, "DoubleClickHandler", picture, headClick);
            Call(pet, "MouseUpHandler", picture, headClick);
            Check(!(bool)Get(pet, "pendingPat"), "Double click queued extra pat");
            Call(pet, "MouseDownHandler", picture, headClick);
            Set(pet, "dragging", true);
            Call(pet, "MouseUpHandler", picture, headClick);
            Check(!(bool)Get(pet, "pendingPat"), "Drag queued pat");
            State(pet, "Bouncing");
            foreach (string name in new[] { "tilt", "cherry", "groom" })
                frames[name].Save("assets/sprites/" + name + "_preview.png");
            CodexStatus.Receive("{\"session_id\":\"ui\",\"turn_id\":\"one\",\"hook_event_name\":\"UserPromptSubmit\"}");
            Call(pet, "CodexTick", null, EventArgs.Empty);
            Check(((Label)Get(pet, "codexLabel")).Text.Contains("工作"), "Status label did not update");
            var busyPosition = pet.Location;
            Call(pet, "MovementTick", null, EventArgs.Empty);
            Check(pet.Location == busyPosition, "Working pet wandered");
            Call(pet, "ToggleSleep");
            CodexStatus.Receive("{\"session_id\":\"ui\",\"turn_id\":\"one\",\"hook_event_name\":\"PermissionRequest\",\"tool_use_id\":\"approval\"}");
            Call(pet, "CodexTick", null, EventArgs.Empty);
            State(pet, "Sleeping");
            Check(((Label)Get(pet, "codexLabel")).Text.Contains("确认"), "Sleeping hid approval status");
        }
        Console.WriteLine("PASS: sleep, wake, paused walking, sleeping drag, feed, bounce interruption, directional expression.");
    }

    private static void FinishAnimation(PetForm pet)
    {
        for (int i = 0; i < 8; i++)
        {
            Set(pet, "nextFrame", DateTime.MinValue);
            Call(pet, "AnimationTick", null, EventArgs.Empty);
        }
    }
}
