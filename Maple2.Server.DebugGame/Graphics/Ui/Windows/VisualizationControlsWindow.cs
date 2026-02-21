using ImGuiNET;
using Maple2.Server.Game.Model;
using System.Numerics;
using Maple2.Server.DebugGame.Graphics.Scene;

namespace Maple2.Server.DebugGame.Graphics.Ui.Windows;

public class VisualizationControlsWindow : IUiWindow {
    public bool AllowMainWindow => false;
    public bool AllowFieldWindow => true;
    public bool Enabled { get; set; } = true;
    public string TypeName => "Visualization";
    public DebugGraphicsContext? Context { get; set; }
    public ImGuiController? ImGuiController { get; set; }
    public DebugFieldWindow? FieldWindow { get; set; }

    public void Initialize(DebugGraphicsContext context, ImGuiController controller, DebugFieldWindow? fieldWindow) {
        Context = context;
        ImGuiController = controller;
        FieldWindow = fieldWindow;
    }

    public void Render() {
        if (FieldWindow?.ActiveRenderer == null) return;
        DebugFieldRenderer renderer = FieldWindow.ActiveRenderer;
        ImGui.Begin("3D Visualization Controls");
        ImGui.Text("Field Visualization Settings");
        ImGui.Separator();
        ImGui.Checkbox("Show Box Colliders", ref renderer.ShowBoxColliders);
        ImGui.Checkbox("Show Vibrate Objects", ref renderer.ShowVibrateObjects);
        ImGui.Checkbox("Show Portals", ref renderer.ShowPortals);
        if (renderer.ShowPortals) {
            ImGui.Indent();
            ImGui.Checkbox("Show Portal Information", ref renderer.ShowPortalInformation);
            ImGui.Checkbox("Show Portal Connections", ref renderer.ShowPortalConnections);
            ImGui.Unindent();
        }
        ImGui.Checkbox("Show Triggers", ref renderer.ShowTriggers);
        if (renderer.ShowTriggers) {
            ImGui.Indent();
            ImGui.Checkbox("Show Trigger Information", ref renderer.ShowTriggerInformation);
            ImGui.Unindent();
        }
        ImGui.Checkbox("Show Sellable Tiles (Plots)", ref renderer.ShowSellableTiles);
        if (renderer.ShowSellableTiles) {
            ImGui.Indent();
            ImGui.Checkbox("Show Plot Labels", ref renderer.ShowPlotLabels);
            ImGui.Unindent();
        }
        ImGui.Checkbox("Show Actors", ref renderer.ShowActors);
        if (renderer.ShowActors) {
            ImGui.Indent();
            ImGui.Checkbox("Show Players", ref renderer.ShowPlayers);
            ImGui.Checkbox("Show NPCs", ref renderer.ShowNpcs);
            ImGui.Checkbox("Show Mobs", ref renderer.ShowMobs);
            ImGui.Unindent();
        }
        ImGui.Checkbox("Show Skill Hitboxes", ref renderer.ShowSkillHitboxes);
        ImGui.Checkbox("Show Field Skill Hitboxes", ref renderer.ShowFieldSkillHitboxes);
        ImGui.Checkbox("Show Cube Skill Hitboxes", ref renderer.ShowCubeSkillHitboxes);
        ImGui.Separator();
        ImGui.Text("Camera Controls:");
        if (ImGui.Button("Reset Camera")) {
            if (renderer.CameraController is FreeCameraController freeCam) freeCam.SetDefaultRotation();
        }
        ImGui.SameLine();
        if (ImGui.Button("Toggle Wireframe")) { Context!.ToggleWireframeMode(); }
        if (ImGui.Button("View Field Overview")) { renderer.SetFieldOverviewCamera(); }
        ImGui.Separator();
        bool isFollowing = renderer.CameraController is FollowCameraController { IsFollowingPlayer: true };
        long? followedId = renderer.CameraController is FollowCameraController fc ? fc.FollowedPlayerId : null;
        ImGui.Text($"Player Follow: {(isFollowing ? $"Player ID: {followedId}" : string.Empty)}");
        if (isFollowing) {
            if (ImGui.Button("Stop Following")) { renderer.StopFollowingPlayer(); }
        } else {
            if (ImGui.Button("Follow First Player")) {
                FieldPlayer? firstPlayer = renderer.Field.Players.Values.FirstOrDefault();
                if (firstPlayer != null) renderer.StartFollowingPlayer(firstPlayer);
            }
        }
        if (isFollowing) {
            Vector3 target = renderer.CameraController switch { FreeCameraController freeCam => freeCam.CameraTarget, FollowCameraController followCam => followCam.CameraTarget, _ => Vector3.Zero };
            ImGui.Text($"Player Following Position: {target}");
        }
        ImGui.Text($"Camera Position: {renderer.CameraController.Camera.Transform.Position}");
        Vector3 forward = renderer.CameraController.Camera.Transform.FrontAxis;
        ImGui.Text($"Camera Forward: X={forward.X:F3}, Y={forward.Y:F3}, Z={forward.Z:F3}");
        ImGui.Text($"Wireframe Mode: {(Context!.WireframeMode ? "ON" : "OFF")}");
        ImGuiController.ClampWindowToViewport();
        ImGui.End();
    }
}
