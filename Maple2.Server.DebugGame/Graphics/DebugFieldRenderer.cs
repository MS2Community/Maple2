using System.Numerics;
using Maple2.Server.Game.DebugGraphics;
using Maple2.Server.Game.Manager.Field;
using ImGuiNET;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Metadata.FieldEntity;
using Maple2.Server.DebugGame.Graphics.Data;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.ActorStateComponent;
using Maple2.Server.Game.Model.Enum;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;
using Maple2.Tools.VectorMath;
using Maple2.Tools.Collision;
using Silk.NET.Maths;
using Maple2.Server.DebugGame.Graphics.Scene;
using Serilog;
using Maple2.Model.Game;

namespace Maple2.Server.DebugGame.Graphics;

public class DebugFieldRenderer : IFieldRenderer {
    public DebugGraphicsContext Context { get; }
    public FieldManager Field { get; }

    // Coordinate system transformation - flip Y/Z axes but keep original scale
    private static readonly Matrix4x4 MapRotation = Matrix4x4.CreateFromAxisAngle(Vector3.UnitX, (float) (-Math.PI / 2));
    private static readonly ILogger Logger = Log.Logger.ForContext<DebugGraphicsContext>();


    public bool IsActive {
        get {
            activeMutex.WaitOne();
            bool isActive = activeWindows.Count > 0;
            activeMutex.ReleaseMutex();
            return isActive;
        }
    }

    private readonly HashSet<DebugFieldWindow> activeWindows = [];
    private readonly Mutex activeMutex = new();

    // Public auto-properties replacing previous private fields + wrappers
    public IActor? SelectedActor;
    public bool ShowBoxColliders = true;
    public bool ShowMeshColliders;
    public bool ShowSpawnPoints;
    public bool ShowVibrateObjects;
    public bool ShowPortals = true;
    public bool ShowPortalInformation = true;
    public bool ShowPortalConnections = true;
    public bool ShowActors = true;
    public bool ShowPlayers = true;
    public bool ShowNpcs = true;
    public bool ShowMobs = true;
    public bool ShowSellableTiles; // toggle for rendering sellable plot tiles
    public bool ShowPlotLabels = true; // toggle for showing floating plot labels (status/owner/etc.)
    public bool ShowTriggers = true;
    public bool ShowTriggerInformation = true;
    public bool ShowSkillHitboxes = true; // toggle for rendering active skill hitboxes
    public bool PlayerMoveMode;
    public bool ForceMove;

    // Entity caching for performance
    private FieldEntity[]? cachedStaticEntities; // Cache static entities (spawn points, triggers, etc.)

    private const float AgentRadiusMeters = 0.3f;
    private const float AgentHeightMeters = 1.4f;
    private const float GameUnitsPerMeter = 100.0f;

    private InstanceBuffer instanceBuffer;
    private SceneViewBuffer sceneViewBuffer;

    // Camera controller interface for all camera functionality
    public ICameraController CameraController { get; private set; }

    // Available controller implementations
    private readonly FreeCameraController freeCameraController;
    private readonly FollowCameraController followCameraController;

    // Shared camera instance
    private readonly Camera sharedCamera = new();

    // Global follow state (independent of active controller)
    public bool HasManuallyStoppedFollowing;
    private bool hasTriedAutoFollow;

    public DebugFieldRenderer(DebugGraphicsContext context, FieldManager field) {
        Context = context;
        Field = field;

        // Initialize camera controllers
        freeCameraController = new FreeCameraController(sharedCamera);
        followCameraController = new FollowCameraController(sharedCamera);

        // Start with free camera controller as default
        CameraController = freeCameraController;

        Vector2D<int> windowSize = Context.DebuggerWindow?.FramebufferSize ?? DebugGraphicsContext.DefaultWindowSize;
        sharedCamera.UpdateProjectionMatrix(windowSize.X, windowSize.Y);
        freeCameraController.SetDefaultRotation(); // Set default rotation for MapleStory 2
    }

    public void Update() {
        if (!IsActive) {
            return;
        }

        if (!Context.HasFieldUpdated(Field)) {
            Context.FieldUpdated(Field);

            Field.Update();
        }

        // Auto-follow first player when field window is first opened or when a new player joins
        // But only if user hasn't manually stopped following
        bool isFollowing = CameraController is FollowCameraController { IsFollowingPlayer: true };
        if (!isFollowing && !HasManuallyStoppedFollowing) {
            FieldPlayer? firstPlayer = Field.Players.Values.FirstOrDefault();
            long? currentFollowedId = CameraController is FollowCameraController fc ? fc.FollowedPlayerId : null;
            if (firstPlayer != null && (!hasTriedAutoFollow || currentFollowedId != firstPlayer.Value.Character.Id)) {
                StartFollowingPlayer(firstPlayer);
                hasTriedAutoFollow = true;
            }
        }

        // Update camera follow if active
        UpdateCameraFollow();
    }

    public void Render(double delta) { }

    // New method specifically for field window 3D rendering
    public void RenderFieldWindow3D(DebugFieldWindow window, double delta) {
        if (!IsActive) {
            return;
        }

        // Handle mouse input for actor selection
        HandleMouseInput();

        // Render 3D field visualization using the DirectX pipeline
        RenderField3DVisualization(window, delta);
    }

    private void HandleMouseInput() {
        // Check if mouse is clicked and we're hovering over the field window
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) {
            // Don't process clicks if ImGui is handling them (mouse over UI elements)
            if (ImGui.GetIO().WantCaptureMouse) {
                return;
            }

            // Get mouse position in screen coordinates
            Vector2 mousePos = ImGui.GetMousePos();

            if (PlayerMoveMode && SelectedActor is FieldPlayer selectedPlayer) {
                // Move mode: try to move the selected player to clicked position
                TryMovePlayerToScreenPosition(selectedPlayer, mousePos);
            } else {
                // Selection mode: try to select an actor at clicked position
                TrySelectActorAtScreenPosition(mousePos);
            }
        }
    }

    public string GetActorName(IActor actor) => actor switch { FieldPlayer player => player.Value.Character.Name, FieldNpc npc => npc.Value.Metadata.Name ?? "Unknown", _ => "Unknown" };
    public string GetActorType(IActor actor) => actor switch { FieldPlayer => "Player", FieldNpc => "NPC", _ => "Unknown" };

    public void CleanUp() { }

    public void AttachWindow(DebugFieldWindow window) {
        activeMutex.WaitOne();

        activeWindows.Add(window);

        activeMutex.ReleaseMutex();

        // Set default camera to field overview when window is first attached
        SetFieldOverviewCamera();

        // Auto-follow first player when field window is opened
        // But only if user hasn't manually stopped following
        bool isFollowing = CameraController is FollowCameraController { IsFollowingPlayer: true };
        if (!isFollowing && !HasManuallyStoppedFollowing) {
            FieldPlayer? firstPlayer = Field.Players.Values.FirstOrDefault();
            if (firstPlayer != null) {
                StartFollowingPlayer(firstPlayer);
            }
        }
    }

    public void DetachWindow(DebugFieldWindow window) {
        activeMutex.WaitOne();

        activeWindows.Remove(window);

        activeMutex.ReleaseMutex();
    }

    private void RenderField3DVisualization(DebugFieldWindow window, double delta) {
        if (Field.AccelerationStructure == null) {
            return;
        }

        UpdateCameraProjection(window);

        // Enable wireframe mode for field visualization
        bool originalWireframeMode = Context.WireframeMode;
        Context.WireframeMode = true;
        Context.SetWireframeRasterizer();

        Context.WireframePipeline!.RenderPass!.VertexShader!.Bind();
        Context.WireframePipeline.RenderPass.PixelShader!.Bind();

        // Render field entities using the DirectX 11 pipeline
        RenderFieldEntities3D(window);

        // Restore original wireframe mode
        Context.WireframeMode = originalWireframeMode;
        Context.SetSolidRasterizer();
    }

    private void RenderFieldEntities3D(DebugFieldWindow window) {
        if (Field.AccelerationStructure == null) {
            return;
        }

        // Render box colliders
        if (ShowBoxColliders) {
            RenderBoxColliders(window);
        }

        // Render mesh colliders
        if (ShowMeshColliders) {
            RenderMeshColliders(window);
        }

        // Render spawn points
        if (ShowSpawnPoints) {
            RenderSpawnPoints(window);
        }

        // Render vibrate objects
        if (ShowVibrateObjects) {
            RenderVibrateObjects(window);
        }

        // Render trigger boxes
        if (ShowTriggers) {
            RenderTriggerBoxes(window);
            if (ShowTriggerInformation) {
                RenderTriggerTextLabels();
            }
        }

        // Render portals
        if (ShowPortals) {
            RenderPortals(window);

            if (ShowPortalConnections) {
                RenderPortalConnectionLines(window);
            }

            // Render portal text labels
            if (ShowPortalInformation) {
                RenderPortalTextLabels();
            }
        }

        // Render sellable tiles (player housing plots / sellable land tiles)
        if (ShowSellableTiles) {
            RenderSellableTiles(window);
            if (ShowPlotLabels) {
                RenderPlotTextLabels();
            }
        }

        // Render actors (players, NPCs, mobs)
        if (ShowActors) {
            RenderActors(window);

            // Render selection highlight for selected actor
            if (SelectedActor != null) {
                RenderActorSelectionHighlight(window);
            }

            // Render text labels above actors using ImGui
            RenderActorTextLabels();
        }

        // Render skill hitboxes
        if (ShowSkillHitboxes) {
            RenderSkillHitboxes(window);
        }
    }

    private void RenderBoxColliders(DebugFieldWindow window) {
        // Set white color for box colliders
        instanceBuffer.Color = new Vector4(1, 1, 1, 1);

        List<FieldBoxColliderEntity> allBoxColliders = GetAllEntities().OfType<FieldBoxColliderEntity>().ToList();

        foreach (FieldBoxColliderEntity boxCollider in allBoxColliders) {
            Transform transform = new() {
                Position = boxCollider.Position,
                RotationAnglesDegrees = boxCollider.Rotation,
            };

            transform.Transformation *= MapRotation;
            instanceBuffer.Transformation = Matrix4x4.Transpose(Matrix4x4.CreateScale(boxCollider.Size) *
                                                                Matrix4x4.CreateTranslation(boxCollider.Position));

            UpdateWireframeInstance(window);
            Context.CoreModels!.WireCube.Draw();
        }
    }

    private void RenderMeshColliders(DebugFieldWindow window) {
        IEnumerable<FieldMeshColliderEntity> allMeshColliders = GetAllEntities().OfType<FieldMeshColliderEntity>();

        foreach (FieldMeshColliderEntity meshCollider in allMeshColliders) {
            // For mesh colliders, render a simple bounding box for now
            Vector3 size = meshCollider.Bounds.Max - meshCollider.Bounds.Min;
            Vector3 center = (meshCollider.Bounds.Max + meshCollider.Bounds.Min) * 0.5f;

            instanceBuffer.Transformation = Matrix4x4.Transpose(Matrix4x4.CreateScale(size) *
                                                                Matrix4x4.CreateTranslation(center));

            UpdateWireframeInstance(window);
            Context.CoreModels!.WireCube.Draw();
        }
    }

    private void RenderSpawnPoints(DebugFieldWindow window) {
        IEnumerable<FieldSpawnTile> allSpawnPoints = GetAllEntities().OfType<FieldSpawnTile>();

        foreach (FieldSpawnTile spawnPoint in allSpawnPoints) {
            instanceBuffer.Transformation = Matrix4x4.Transpose(Matrix4x4.CreateScale(Vector3.One * 50.0f) *
                                                                Matrix4x4.CreateTranslation(spawnPoint.Position));

            UpdateWireframeInstance(window);
            Context.CoreModels!.WireCube.Draw();
        }
    }

    private void RenderVibrateObjects(DebugFieldWindow window) {
        // Set pink color for vibrate objects
        instanceBuffer.Color = new Vector4(1, 0.75f, 0.8f, 1);

        foreach (FieldVibrateEntity vibrateEntity in Field.AccelerationStructure!.VibrateEntities) {
            instanceBuffer.Transformation = Matrix4x4.Transpose(Matrix4x4.CreateScale(Vector3.One * 100.0f) *
                                                                Matrix4x4.CreateTranslation(vibrateEntity.Position));

            UpdateWireframeInstance(window);
            Context.CoreModels!.WireCube.Draw();
        }
    }

    private void RenderPortals(DebugFieldWindow window) {
        // Set blue color for portals
        instanceBuffer.Color = new Vector4(0, 0.5f, 1, 1);
        foreach (FieldPortal portal in Field.GetPortals()) {
            Vector3 size = portal.Value.Dimension != Vector3.Zero ? portal.Value.Dimension : Vector3.One * 100.0f;
            instanceBuffer.Transformation = Matrix4x4.Transpose(Matrix4x4.CreateScale(size) *
                                                                Matrix4x4.CreateTranslation(portal.Position));
            UpdateWireframeInstance(window);
            Context.CoreModels!.WireCube.Draw();
        }
    }

    private void RenderSellableTiles(DebugFieldWindow window) {
        if (Field.AccelerationStructure == null) return;
        IEnumerable<FieldSellableTile> tiles = GetAllEntities().OfType<FieldSellableTile>();

        // Simple deterministic color per group (hash -> color)
        Vector4 ColorForGroup(int group) {
            unchecked {
                int hash = (int) (group * 2654435761u); // Knuth multiplicative hash (32-bit wrap)
                // extract bytes
                float r = ((hash >> 16) & 0xFF) / 255f;
                float g = ((hash >> 8) & 0xFF) / 255f;
                float b = (hash & 0xFF) / 255f;
                // brighten a bit
                float scale = 0.6f + 0.4f * ((hash >> 24) & 0xFF) / 255f;
                return new Vector4(r * scale, g * scale, b * scale, 0.45f); // semi-transparent
            }
        }

        foreach (FieldSellableTile tile in tiles) {
            // Color by group
            instanceBuffer.Color = ColorForGroup(tile.SellableGroup);
            Vector3 size = tile.Bounds.Max - tile.Bounds.Min;
            Vector3 center = (tile.Bounds.Max + tile.Bounds.Min) * 0.5f;
            center.Z -= size.Z * 0.5f; // shift down half-height
            instanceBuffer.Transformation = Matrix4x4.Transpose(Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(center));

            UpdateWireframeInstance(window);
            Context.CoreModels!.WireCube.Draw();
        }
    }

    private void RenderPortalConnectionLines(DebugFieldWindow window) {
        // Set cyan color for portal connection lines
        instanceBuffer.Color = new Vector4(0, 1, 1, 0.7f);

        List<FieldPortal> portals = Field.GetPortals().ToList();

        foreach (FieldPortal sourcePortal in portals) {
            // Only draw lines for portals that target the same field and have a specific target portal
            if (sourcePortal.Value.TargetMapId == Field.MapId && sourcePortal.Value.TargetPortalId > 0) {
                // Find the target portal in the same field
                FieldPortal? targetPortal = portals.FirstOrDefault(p => p.Value.Id == sourcePortal.Value.TargetPortalId);

                if (targetPortal != null) {
                    // Draw a line between the two portals
                    RenderLine(window, sourcePortal.Position, targetPortal.Position);
                }
            }
        }
    }

    private void RenderLine(DebugFieldWindow window, Vector3 start, Vector3 end) {
        // Create a line by drawing a thin cylinder between two points
        Vector3 direction = end - start;
        float distance = direction.Length();

        if (distance < 0.1f) return; // Skip very short lines

        Vector3 center = (start + end) * 0.5f;
        Vector3 normalizedDirection = Vector3.Normalize(direction);

        // Calculate rotation to align cylinder with the line direction
        // The cylinder model is oriented along the Y axis by default
        Vector3 defaultUp = Vector3.UnitY;
        Matrix4x4 rotationMatrix;

        // Check if direction is parallel to Y axis
        float dot = Vector3.Dot(defaultUp, normalizedDirection);
        if (Math.Abs(dot) > 0.999f) {
            // Direction is parallel to Y axis, no rotation needed (or 180 degrees)
            rotationMatrix = dot > 0 ? Matrix4x4.Identity : Matrix4x4.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);
        } else {
            // Calculate rotation between Y axis and direction
            Vector3 axis = Vector3.Normalize(Vector3.Cross(defaultUp, normalizedDirection));
            float angle = MathF.Acos(Math.Clamp(dot, -1.0f, 1.0f));
            rotationMatrix = Matrix4x4.CreateFromAxisAngle(axis, angle);
        }

        instanceBuffer.Transformation = Matrix4x4.Transpose(Matrix4x4.CreateScale(new Vector3(10.0f, distance / 2.0f, 10.0f)) *
                                                            rotationMatrix *
                                                            Matrix4x4.CreateTranslation(center));

        UpdateWireframeInstance(window);
        Context.CoreModels!.Cylinder.Draw();
    }

    private void RenderActors(DebugFieldWindow window) {
        const float agentRadius = AgentRadiusMeters * GameUnitsPerMeter;
        const float agentHeight = AgentHeightMeters * GameUnitsPerMeter;

        if (ShowPlayers) {
            instanceBuffer.Color = new Vector4(0, 1, 1, 1); // Cyan for players
            foreach ((int _, FieldPlayer player) in Field.Players) {
                // Rotate cylinder 90 degrees around X axis to make it stand upright (Y axis becomes Z axis)
                var rotation = Matrix4x4.CreateRotationX((float) (Math.PI / 2));
                instanceBuffer.Transformation = Matrix4x4.Transpose(Matrix4x4.CreateScale(new Vector3(agentRadius, agentHeight, agentRadius)) *
                                                                    rotation *
                                                                    Matrix4x4.CreateTranslation(player.Position));

                UpdateWireframeInstance(window);
                Context.CoreModels!.Cylinder.Draw();
            }
        }

        if (ShowNpcs) {
            foreach ((int _, FieldNpc npc) in Field.Npcs) {
                // Set color based on dead status
                if (npc.IsDead) {
                    instanceBuffer.Color = new Vector4(0.5f, 0.5f, 0.5f, 0.3f); // Gray and semi-transparent for dead NPCs
                } else {
                    instanceBuffer.Color = new Vector4(0, 1, 0, 1); // Green for alive NPCs
                }

                // Rotate cylinder 90 degrees around X axis to make it stand upright (Y axis becomes Z axis)
                var rotation = Matrix4x4.CreateRotationX((float) (Math.PI / 2));
                instanceBuffer.Transformation = Matrix4x4.Transpose(Matrix4x4.CreateScale(new Vector3(agentRadius, agentHeight, agentRadius)) *
                                                                    rotation *
                                                                    Matrix4x4.CreateTranslation(npc.Position));

                UpdateWireframeInstance(window);
                Context.CoreModels!.Cylinder.Draw(); // Use cylinder for NPCs
            }
        }


        if (ShowMobs) {
            foreach ((int _, FieldNpc mob) in Field.Mobs) {
                // Set color based on dead status and battle state
                if (mob.IsDead) {
                    instanceBuffer.Color = new Vector4(0.5f, 0.5f, 0.5f, 0.3f); // Gray and semi-transparent for dead mobs
                } else {
                    // Check if mob is aggressive (in battle) or wandering
                    if (mob.BattleState.InBattle) {
                        instanceBuffer.Color = new Vector4(1, 0, 0, 1); // Red for aggressive mobs
                    } else {
                        instanceBuffer.Color = new Vector4(1, 1, 0, 1); // Yellow for wandering mobs
                    }
                }

                // Rotate cylinder 90 degrees around X axis to make it stand upright (Y axis becomes Z axis)
                var rotation = Matrix4x4.CreateRotationX((float) (Math.PI / 2));
                instanceBuffer.Transformation = Matrix4x4.Transpose(Matrix4x4.CreateScale(new Vector3(agentRadius, agentHeight, agentRadius)) *
                                                                    rotation *
                                                                    Matrix4x4.CreateTranslation(mob.Position));

                UpdateWireframeInstance(window);
                Context.CoreModels!.Cylinder.Draw();
            }
        }
    }

    private void RenderActorSelectionHighlight(DebugFieldWindow window) {
        if (SelectedActor == null) return;

        const float highlightRadius = AgentRadiusMeters * GameUnitsPerMeter * 1.4f;
        const float highlightHeight = AgentHeightMeters * GameUnitsPerMeter * 1.2f;

        // Rotate cylinder 90 degrees around X axis to make it stand upright (Y axis becomes Z axis)
        var rotation = Matrix4x4.CreateRotationX((float) (Math.PI / 2));

        // Render larger outline
        instanceBuffer.Color = new Vector4(1, 1, 0, 1); // Yellow

        instanceBuffer.Transformation = Matrix4x4.Transpose(Matrix4x4.CreateScale(new Vector3(highlightRadius, highlightHeight, highlightRadius)) *
                                                            rotation *
                                                            Matrix4x4.CreateTranslation(SelectedActor.Position));

        UpdateWireframeInstance(window);
        Context.CoreModels!.Cylinder.Draw();
    }

    public void SetFieldOverviewCamera() {
        // Stop following player when switching to field overview
        if (CameraController is FollowCameraController { IsFollowingPlayer: true }) {
            StopFollowingPlayer();
        }

        List<FieldBoxColliderEntity> allColliders = GetAllEntities().OfType<FieldBoxColliderEntity>().ToList();
        if (allColliders.Count > 0) {
            // Calculate field bounds in map coordinates
            Vector3 min = allColliders[0].Position;
            Vector3 max = allColliders[0].Position;

            foreach (FieldBoxColliderEntity collider in allColliders) {
                min = Vector3.Min(min, collider.Position - collider.Size * 0.5f);
                max = Vector3.Max(max, collider.Position + collider.Size * 0.5f);
            }

            // Transform to visualization coordinates
            Vector3 centerMap = (min + max) * 0.5f;
            Vector3 sizeMap = max - min;

            Vector3 center = Vector3.Transform(centerMap, MapRotation);
            float distance = Math.Max(sizeMap.X, Math.Max(sizeMap.Y, sizeMap.Z)) * 1.5f;

            // Set camera target and position for field overview
            if (CameraController is FreeCameraController freeCam) {
                freeCam.SetCameraTarget(center);
                freeCam.SetFieldOverviewRotation();
                CameraController.Camera.Transform.Position = center + new Vector3(0, -distance * 0.7f, distance * 0.7f);
            }
        } else {
            // Fallback if no colliders found
            if (CameraController is FreeCameraController freeCam) {
                freeCam.SetCameraTarget(Vector3.Zero);
                freeCam.SetFieldOverviewRotation();
                CameraController.Camera.Transform.Position = new Vector3(0, -1000, 1000);
            }
        }
    }

    private void UpdateCameraFollow() {
        if (CameraController is not FollowCameraController { IsFollowingPlayer: true } followCam || followCam.FollowedPlayerId == null) {
            return;
        }

        // Find the player being followed
        FieldPlayer? followedPlayer = Field.Players.Values.FirstOrDefault(p => p.Value.Character.Id == followCam.FollowedPlayerId);
        if (followedPlayer == null) {
            // Player not found, stop following
            StopFollowingPlayer();
            return;
        }

        // Update camera to follow player position using the camera controller
        Vector3 playerPosition = followedPlayer.Position;
        UpdatePlayerFollow(playerPosition);
    }

    private Vector2D<int> GetFieldWindowSize() {
        // Get the size from the first active field window, or use default
        activeMutex.WaitOne();
        try {
            DebugFieldWindow? firstWindow = activeWindows.FirstOrDefault();
            if (firstWindow?.DebuggerWindow != null) {
                return firstWindow.DebuggerWindow.FramebufferSize;
            }
        } finally {
            activeMutex.ReleaseMutex();
        }

        // Fallback to default field window size
        return DebugGraphicsContext.DefaultFieldWindowSize;
    }

    private bool TryWorldToScreen(Vector3 worldPos, out Vector2 screenPos) {
        screenPos = Vector2.Zero;

        // Transform world position to screen coordinates
        Matrix4x4 viewMatrix = CameraController.Camera.ViewMatrix;
        Matrix4x4 projMatrix = CameraController.Camera.ProjectionMatrix;
        Matrix4x4 viewProjMatrix = viewMatrix * projMatrix;

        // Transform to clip space
        Vector4 clipPos = Vector4.Transform(new Vector4(worldPos, 1.0f), viewProjMatrix);

        // Check if behind camera
        if (clipPos.W <= 0) return false;

        // Perspective divide to get NDC coordinates
        Vector3 ndcPos = new Vector3(clipPos.X, clipPos.Y, clipPos.Z) / clipPos.W;

        // Check if outside screen bounds (with some tolerance)
        if (ndcPos.X < -1.2f || ndcPos.X > 1.2f || ndcPos.Y < -1.2f || ndcPos.Y > 1.2f) return false;

        // Get the actual viewport size from the field window (not main window)
        Vector2D<int> windowSize = GetFieldWindowSize();

        // Convert NDC to screen coordinates
        // NDC: (-1, -1) = bottom-left, (1, 1) = top-right
        // Screen: (0, 0) = top-left, (width, height) = bottom-right
        float screenX = (ndcPos.X + 1.0f) * 0.5f * windowSize.X;
        float screenY = (1.0f - ndcPos.Y) * 0.5f * windowSize.Y; // Flip Y axis for screen coordinates

        screenPos = new Vector2(screenX, screenY);
        return true;
    }

    private bool TryScreenToWorldRay(Vector2 screenPos, out Vector3 rayOrigin, out Vector3 rayDirection) {
        rayOrigin = Vector3.Zero;
        rayDirection = Vector3.Zero;

        // Get the actual viewport size from the field window
        Vector2D<int> windowSize = GetFieldWindowSize();

        // Convert screen coordinates to NDC coordinates
        // Screen: (0, 0) = top-left, (width, height) = bottom-right
        // NDC: (-1, -1) = bottom-left, (1, 1) = top-right
        float ndcX = (screenPos.X / windowSize.X) * 2.0f - 1.0f;
        float ndcY = 1.0f - (screenPos.Y / windowSize.Y) * 2.0f; // Flip Y axis

        // Get camera matrices
        Matrix4x4 viewMatrix = CameraController.Camera.ViewMatrix;
        Matrix4x4 projMatrix = CameraController.Camera.ProjectionMatrix;

        // Calculate inverse view-projection matrix
        Matrix4x4 viewProjMatrix = viewMatrix * projMatrix;
        if (!Matrix4x4.Invert(viewProjMatrix, out Matrix4x4 invViewProjMatrix)) {
            return false; // Matrix is not invertible
        }

        // Create two points: one on near plane, one on far plane
        Vector4 nearPoint = new Vector4(ndcX, ndcY, -1.0f, 1.0f); // Near plane in NDC
        Vector4 farPoint = new Vector4(ndcX, ndcY, 1.0f, 1.0f); // Far plane in NDC

        // Transform to world space
        Vector4 nearWorld = Vector4.Transform(nearPoint, invViewProjMatrix);
        Vector4 farWorld = Vector4.Transform(farPoint, invViewProjMatrix);

        // Perspective divide
        if (nearWorld.W == 0 || farWorld.W == 0) return false;
        nearWorld /= nearWorld.W;
        farWorld /= farWorld.W;

        // Calculate ray
        rayOrigin = new Vector3(nearWorld.X, nearWorld.Y, nearWorld.Z);
        Vector3 farWorldPos = new Vector3(farWorld.X, farWorld.Y, farWorld.Z);
        rayDirection = Vector3.Normalize(farWorldPos - rayOrigin);

        return true;
    }

    private bool TrySelectActorAtScreenPosition(Vector2 screenPos) {
        if (!TryScreenToWorldRay(screenPos, out Vector3 rayOrigin, out Vector3 rayDirection)) {
            return false;
        }

        IActor? closestActor = null;
        float closestDistance = float.MaxValue;

        // Test players
        if (ShowPlayers) {
            foreach ((int _, FieldPlayer player) in Field.Players) {
                if (TryRayPrismIntersection(rayOrigin, rayDirection, player.Shape, out float distance)) {
                    if (distance < closestDistance) {
                        closestDistance = distance;
                        closestActor = player;
                    }
                }
            }
        }

        // Test NPCs
        if (ShowNpcs) {
            foreach ((int _, FieldNpc npc) in Field.Npcs) {
                if (TryRayPrismIntersection(rayOrigin, rayDirection, npc.Shape, out float distance)) {
                    if (distance < closestDistance) {
                        closestDistance = distance;
                        closestActor = npc;
                    }
                }
            }
        }

        // Test Mobs
        if (ShowMobs) {
            foreach ((int _, FieldNpc mob) in Field.Mobs) {
                if (TryRayPrismIntersection(rayOrigin, rayDirection, mob.Shape, out float distance)) {
                    if (distance < closestDistance) {
                        closestDistance = distance;
                        closestActor = mob;
                    }
                }
            }
        }

        if (closestActor != null) {
            SelectedActor = closestActor;
            return true;
        }

        return false;
    }

    private bool TryRayPrismIntersection(Vector3 rayOrigin, Vector3 rayDirection, IPrism prism, out float distance) {
        distance = 0;

        // Sample points along the ray to test for intersection with the prism
        // This is a simple approach - for more accuracy, you could implement proper ray-prism intersection
        const float stepSize = 10.0f; // Step size in world units
        const float maxDistance = 10000.0f; // Maximum ray distance to test

        for (float t = 0; t < maxDistance; t += stepSize) {
            Vector3 testPoint = rayOrigin + t * rayDirection;

            if (prism.Contains(testPoint)) {
                distance = t;
                return true;
            }
        }

        return false;
    }

    private void TryMovePlayerToScreenPosition(FieldPlayer player, Vector2 screenPos) {
        if (!TryScreenToWorldRay(screenPos, out Vector3 rayOrigin, out Vector3 rayDirection)) {
            return;
        }

        // Cast ray down to find ground intersection
        Vector3? targetPosition = FindGroundIntersection(rayOrigin, rayDirection);

        if (targetPosition.HasValue) {
            Vector3 newPosition = targetPosition.Value;
            Vector3 currentRotation = player.Rotation; // Keep current rotation

            if (ForceMove) {
                // Force move: bypass validation and send packet directly
                player.Session.Send(PortalPacket.MoveByPortal(player, newPosition, currentRotation));
            } else {
                // Normal move: use the built-in function with validation
                player.MoveToPosition(newPosition, currentRotation);
            }
        }
    }

    private Vector3? FindGroundIntersection(Vector3 rayOrigin, Vector3 rayDirection) {
        // First, get the X,Y position where the ray would hit a horizontal plane
        Vector2 targetXy = GetRayGroundProjection(rayOrigin, rayDirection);

        // Find the topmost block at this X,Y position
        float? topBlockZ = FindTopmostBlockAt(targetXy);

        if (topBlockZ.HasValue) {
            // Place player on top of the highest block (add some height for the player)
            return new Vector3(targetXy.X, targetXy.Y, topBlockZ.Value + 100.0f); // 50 units above block
        }
        // No blocks found - drop player from way above for natural falling
        const float dropHeight = 2000.0f; // Drop from 2000 units above
        return new Vector3(targetXy.X, targetXy.Y, dropHeight);
    }

    private Vector2 GetRayGroundProjection(Vector3 rayOrigin, Vector3 rayDirection) {
        // Project the ray to a reasonable ground level to get X,Y coordinates
        float targetZ = SelectedActor?.Position.Z ?? 0.0f;

        if (Math.Abs(rayDirection.Z) > 0.001f) {
            float t = (targetZ - rayOrigin.Z) / rayDirection.Z;
            if (t > 0) {
                Vector3 intersection = rayOrigin + t * rayDirection;
                return new Vector2(intersection.X, intersection.Y);
            }
        }

        // Fallback: project ray forward a reasonable distance
        Vector3 projected = rayOrigin + rayDirection * 1000.0f;
        return new Vector2(projected.X, projected.Y);
    }

    private float? FindTopmostBlockAt(Vector2 position) {
        if (Field.AccelerationStructure == null) {
            return null;
        }

        float? highestZ = null;

        // Query box colliders (main map blocks) at this position
        var boxColliders = GetAllEntities().OfType<FieldBoxColliderEntity>();

        foreach (FieldBoxColliderEntity collider in boxColliders) {
            // Check if this collider contains the X,Y position
            if (IsPositionInBoxCollider(position, collider)) {
                // Calculate the top of this collider
                float colliderTop = collider.Position.Z + (collider.Size.Z * 0.5f);

                if (!highestZ.HasValue || colliderTop > highestZ.Value) {
                    highestZ = colliderTop;
                }
            }
        }

        return highestZ;
    }

    private bool IsPositionInBoxCollider(Vector2 position, FieldBoxColliderEntity collider) {
        // Simple AABB check for now - could be enhanced for rotated boxes
        Vector3 min = collider.Position - (collider.Size * 0.5f);
        Vector3 max = collider.Position + (collider.Size * 0.5f);

        return position.X >= min.X && position.X <= max.X &&
               position.Y >= min.Y && position.Y <= max.Y;
    }

    private void RenderActorTextLabels() {
        ImDrawListPtr drawList = ImGui.GetBackgroundDrawList();

        // Render player names
        if (ShowPlayers) {
            foreach ((int _, FieldPlayer player) in Field.Players) {
                Vector3 textPos = player.Position + new Vector3(0, 0, 200); // Above the cylinder
                if (!TryWorldToScreen(textPos, out Vector2 screenPos)) continue;

                string playerName = player.Value.Character.Name;
                bool isSelected = SelectedActor == player;

                Vector2 textSize = ImGui.CalcTextSize(playerName);
                float left = screenPos.X - (textSize.X * 0.5f);
                Vector2 textTopLeft = new(left, screenPos.Y);
                Vector4 textColor = isSelected ? new Vector4(1, 1, 0, 1) : new Vector4(0, 1, 1, 1); // Yellow if selected, cyan otherwise
                Vector4 bgColor = isSelected ? new Vector4(0.2f, 0.2f, 0, 0.9f) : new Vector4(0, 0, 0, 0.7f); // Darker yellow bg if selected
                Vector4 borderColor = isSelected ? new Vector4(1, 1, 0, 1) : new Vector4(0.2f, 0.2f, 0.2f, 0.8f); // Yellow border if selected

                // Draw background centered
                Vector2 bgMin = textTopLeft - new Vector2(4, 2);
                Vector2 bgMax = textTopLeft + textSize + new Vector2(4, 2);
                drawList.AddRectFilled(bgMin, bgMax, ImGui.ColorConvertFloat4ToU32(bgColor));
                drawList.AddRect(bgMin, bgMax, ImGui.ColorConvertFloat4ToU32(borderColor));

                // Draw text on top
                drawList.AddText(textTopLeft, ImGui.ColorConvertFloat4ToU32(textColor), playerName);
            }
        }

        // Render NPC names
        if (ShowNpcs) {
            foreach ((int _, FieldNpc npc) in Field.Npcs) {
                Vector3 textPos = npc.Position + new Vector3(0, 0, 200); // Above the cylinder
                if (!TryWorldToScreen(textPos, out Vector2 screenPos)) continue;

                string npcName = npc.Value.Metadata.Name ?? $"NPC {npc.Value.Metadata.Id}";
                bool isSelected = SelectedActor == npc;

                Vector4 textColor = isSelected ? new Vector4(1, 1, 0, 1) : new Vector4(0, 1, 0, 1); // Yellow if selected, green otherwise
                Vector4 bgColor = isSelected ? new Vector4(0.2f, 0.2f, 0, 0.9f) : new Vector4(0, 0, 0, 0.7f); // Darker yellow bg if selected
                Vector4 borderColor = isSelected ? new Vector4(1, 1, 0, 1) : new Vector4(0.2f, 0.2f, 0.2f, 0.8f); // Yellow border if selected
                uint color = ImGui.ColorConvertFloat4ToU32(textColor);

                Vector2 nameSize = ImGui.CalcTextSize(npcName);
                float left = screenPos.X - (nameSize.X * 0.5f);
                Vector2 nameTopLeft = new(left, screenPos.Y);
                Vector2 nameBgMin = nameTopLeft - new Vector2(4, 2);
                Vector2 nameBgMax = nameTopLeft + nameSize + new Vector2(4, 2);
                drawList.AddRectFilled(nameBgMin, nameBgMax, ImGui.ColorConvertFloat4ToU32(bgColor));
                drawList.AddRect(nameBgMin, nameBgMax, ImGui.ColorConvertFloat4ToU32(borderColor));
                drawList.AddText(nameTopLeft, color, npcName);
            }
        }

        // Render Mob names and HP
        if (ShowMobs) {
            foreach ((int _, FieldNpc mob) in Field.Mobs) {
                Vector3 textPos = mob.Position + new Vector3(0, 0, 200); // Above the cylinder
                if (!TryWorldToScreen(textPos, out Vector2 screenPos)) continue;

                string mobName = mob.Value.Metadata.Name ?? $"Mob {mob.Value.Metadata.Id}";
                bool isSelected = SelectedActor == mob;

                // Calculate HP percentage
                long currentHp = mob.Stats.Values[BasicAttribute.Health].Current;
                long maxHp = mob.Stats.Values[BasicAttribute.Health].Total;
                float hpPercent = maxHp > 0 ? (float) currentHp / maxHp * 100f : 0f;
                string hpText = $"HP: {hpPercent:F1}%";

                // Choose color based on status, but override with yellow if selected
                Vector4 textColor;
                Vector4 bgColor;
                Vector4 borderColor;

                if (isSelected) {
                    textColor = new Vector4(1, 1, 0, 1); // Yellow for selected
                    bgColor = new Vector4(0.2f, 0.2f, 0, 0.9f); // Darker yellow bg
                    borderColor = new Vector4(1, 1, 0, 1); // Yellow border
                } else {
                    // Normal colors based on mob status
                    if (mob.IsDead) {
                        textColor = new Vector4(0.5f, 0.5f, 0.5f, 0.7f); // Gray for dead
                    } else if (mob.BattleState.InBattle) {
                        textColor = new Vector4(1, 0, 0, 1); // Red for aggressive
                    } else {
                        textColor = new Vector4(1, 1, 0, 1); // Yellow for wandering
                    }
                    bgColor = new Vector4(0, 0, 0, 0.7f); // Normal dark background
                    borderColor = new Vector4(0.2f, 0.2f, 0.2f, 0.8f); // Normal border
                }

                uint color = ImGui.ColorConvertFloat4ToU32(textColor);

                Vector2 nameSize = ImGui.CalcTextSize(mobName);
                Vector2 hpSize = ImGui.CalcTextSize(hpText);
                float maxWidth = MathF.Max(nameSize.X, hpSize.X);
                float left = screenPos.X - (maxWidth * 0.5f);
                Vector2 namePos = new(left + (maxWidth - nameSize.X) * 0.5f, screenPos.Y);
                Vector2 hpPos = new(left + (maxWidth - hpSize.X) * 0.5f, namePos.Y + nameSize.Y + 4);

                // Background covering both lines
                Vector2 bgMin = new(left - 4, screenPos.Y - 2);
                Vector2 bgMax = new(left + maxWidth + 4, hpPos.Y + hpSize.Y + 2);
                drawList.AddRectFilled(bgMin, bgMax, ImGui.ColorConvertFloat4ToU32(bgColor));
                drawList.AddRect(bgMin, bgMax, ImGui.ColorConvertFloat4ToU32(borderColor));

                drawList.AddText(namePos, color, mobName);
                drawList.AddText(hpPos, color, hpText);
            }
        }
    }

    private void RenderPortalTextLabels() {
        ImDrawListPtr drawList = ImGui.GetBackgroundDrawList();

        // Render portal information
        foreach (FieldPortal portal in Field.GetPortals()) {
            Vector3 textPos = portal.Position + new Vector3(0, 0, 150); // Above the portal cube
            if (!TryWorldToScreen(textPos, out Vector2 screenPos)) continue;

            string enabledText = $"{(portal.Enabled ? "Enabled" : "Disabled")} | {(portal.Visible ? "Visible" : "Hidden")}";
            string idText = $"ID: {portal.Value.Id}";
            string targetText = $"Target: {portal.Value.TargetMapId}";
            string targetPortalText = $"Target Portal: {portal.Value.TargetPortalId}";
            string typeText = $"Type: {portal.Value.Type}";

            Vector4 textColor = portal.Enabled ? new Vector4(0, 0.8f, 1, 1) : new Vector4(0.6f, 0.6f, 0.6f, 1);
            uint color = ImGui.ColorConvertFloat4ToU32(textColor);

            Vector2 enabledSize = ImGui.CalcTextSize(enabledText);
            Vector2 idSize = ImGui.CalcTextSize(idText);
            Vector2 targetSize = ImGui.CalcTextSize(targetText);
            Vector2 targetPortalSize = ImGui.CalcTextSize(targetPortalText);
            Vector2 typeSize = ImGui.CalcTextSize(typeText);

            float maxWidth = Math.Max(Math.Max(Math.Max(enabledSize.X, idSize.X), Math.Max(targetSize.X, targetPortalSize.X)), typeSize.X);
            float totalHeight = enabledSize.Y + idSize.Y + targetSize.Y + targetPortalSize.Y + typeSize.Y + 8;
            float left = screenPos.X - (maxWidth * 0.5f);

            Vector2 bgMin = new(left - 4, screenPos.Y - 2);
            Vector2 bgMax = new(left + maxWidth + 4, screenPos.Y + totalHeight + 4);
            drawList.AddRectFilled(bgMin, bgMax, ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.8f)));
            drawList.AddRect(bgMin, bgMax, ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.3f, 0.3f, 0.9f)));

            Vector2 currentPos = new(left, screenPos.Y);
            drawList.AddText(currentPos, color, enabledText);
            currentPos.Y += enabledSize.Y + 1;
            drawList.AddText(currentPos, color, idText);
            currentPos.Y += idSize.Y + 1;
            drawList.AddText(currentPos, color, targetText);
            currentPos.Y += targetSize.Y + 1;
            drawList.AddText(currentPos, color, targetPortalText);
            currentPos.Y += targetPortalSize.Y + 1;
            drawList.AddText(currentPos, color, typeText);
        }
    }

    private void RenderPlotTextLabels() {
        if (!ShowSellableTiles || !ShowPlotLabels) return;
        if (Field.AccelerationStructure == null) return;

        // We'll aggregate one label per SellableGroup using the first tile's center.
        // Map group -> representative tile center & vertical extent
        var groupInfo = new Dictionary<int, (Vector3 center, float maxZ)>();
        foreach (FieldEntity entity in GetAllEntities()) {
            if (entity is not FieldSellableTile tile) continue;
            Vector3 size = tile.Bounds.Max - tile.Bounds.Min;
            Vector3 center = (tile.Bounds.Max + tile.Bounds.Min) * 0.5f;
            center.Z -= size.Z * 0.5f; // match visual cube shift
            if (groupInfo.TryGetValue(tile.SellableGroup, out var info)) {
                // Average centers and track max Z for label stacking
                Vector3 avg = (info.center + center) * 0.5f;
                groupInfo[tile.SellableGroup] = (avg, MathF.Max(info.maxZ, center.Z + size.Z));
            } else {
                groupInfo[tile.SellableGroup] = (center, center.Z + size.Z);
            }
        }

        if (groupInfo.Count == 0) return;

        ImDrawListPtr drawList = ImGui.GetBackgroundDrawList();

        // Attempt to look up plot metadata via Field.Plots (number == SellableGroup)
        foreach ((int group, (Vector3 baseCenter, float topZ)) in groupInfo) {
            Vector3 labelWorld = new(baseCenter.X, baseCenter.Y, topZ + 120f);
            if (!TryWorldToScreen(labelWorld, out Vector2 screenPos)) continue;

            Plot? plot = null;
            if (Field.Plots.ContainsKey(group)) {
                plot = Field.Plots[group];
            }
            string state = plot?.State.ToString() ?? "Unknown";
            long ownerId = plot?.OwnerId ?? 0;
            long expiry = plot?.ExpiryTime ?? 0;
            string ownerText = ownerId == 0 ? "Unowned" : ownerId.ToString();
            string expiryText = expiry <= 0 ? "-" : DateTimeOffset.FromUnixTimeSeconds(expiry).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            string name = plot?.Name ?? string.Empty;
            string header = string.IsNullOrEmpty(name) ? $"Plot {group}" : name;
            string line2 = $"State: {state} | Owner: {ownerText}";
            string line3 = $"Expiry: {expiryText}";

            Vector2 headerSize = ImGui.CalcTextSize(header);
            Vector2 l2Size = ImGui.CalcTextSize(line2);
            Vector2 l3Size = ImGui.CalcTextSize(line3);
            float maxWidth = MathF.Max(headerSize.X, MathF.Max(l2Size.X, l3Size.X));
            float totalHeight = headerSize.Y + l2Size.Y + l3Size.Y + 8;
            float left = screenPos.X - (maxWidth * 0.5f);

            Vector4 boxColor = new(0, 0, 0, 0.65f);
            Vector4 borderColor = new(0.3f, 0.3f, 0.3f, 0.9f);
            Vector4 headerColor = new(1f, 0.85f, 0.3f, 1f);
            Vector4 textColor = new(0.9f, 0.9f, 0.9f, 1f);

            Vector2 bgMin = new(left - 4, screenPos.Y - 2);
            Vector2 bgMax = new(left + maxWidth + 4, screenPos.Y + totalHeight + 4);
            drawList.AddRectFilled(bgMin, bgMax, ImGui.ColorConvertFloat4ToU32(boxColor));
            drawList.AddRect(bgMin, bgMax, ImGui.ColorConvertFloat4ToU32(borderColor));

            Vector2 cursor = new(left, screenPos.Y);
            drawList.AddText(cursor, ImGui.ColorConvertFloat4ToU32(headerColor), header);
            cursor.Y += headerSize.Y + 1;
            drawList.AddText(cursor, ImGui.ColorConvertFloat4ToU32(textColor), line2);
            cursor.Y += l2Size.Y + 1;
            drawList.AddText(cursor, ImGui.ColorConvertFloat4ToU32(textColor), line3);
        }
    }

    private FieldEntity[] GetAllEntities() {
        // Cache static entities since they never change during field runtime
        if (cachedStaticEntities != null) {
            return cachedStaticEntities;
        }

        if (Field.AccelerationStructure == null) {
            cachedStaticEntities = [];
            return cachedStaticEntities;
        }

        var allEntities = new List<FieldEntity>();

        // Add all aligned entities
        allEntities.AddRange(Field.AccelerationStructure.AlignedEntities.ToArray());

        // Add all aligned trimmed entities
        allEntities.AddRange(Field.AccelerationStructure.AlignedTrimmedEntities.ToArray());

        // Add all unaligned entities
        allEntities.AddRange(Field.AccelerationStructure.UnalignedEntities.ToArray());

        // Add vibrate entities (they are a special type)
        allEntities.AddRange(Field.AccelerationStructure.VibrateEntities.ToArray());

        // Cache the result for future calls
        cachedStaticEntities = allEntities.ToArray();
        return cachedStaticEntities;
    }

    private void UpdateCameraProjection(DebugFieldWindow window) {
        Vector2D<int> windowSize = Context.DebuggerWindow?.FramebufferSize ?? DebugGraphicsContext.DefaultWindowSize;
        CameraController.Camera.UpdateProjectionMatrix(windowSize.X, windowSize.Y);
        sceneViewBuffer.ViewMatrix = Matrix4x4.Transpose(CameraController.Camera.ViewMatrix);
        sceneViewBuffer.ProjectionMatrix = Matrix4x4.Transpose(CameraController.Camera.ProjectionMatrix);

        window.SceneViewConstantBuffer.Update(in sceneViewBuffer);

        window.SceneState.BindConstantBuffer(window.SceneViewConstantBuffer, 0, Enum.ShaderStageFlags.Vertex);
        window.SceneState.UpdateBindings();
    }

    /// <summary>
    /// Switches to the free camera controller
    /// </summary>
    private void SwitchToFreeCameraController() {
        if (CameraController != freeCameraController) {
            CameraController = freeCameraController;
            Logger.Information("Switched to free camera controller");
        } else {
            Logger.Information("Already using FreeCameraController - no switch needed");
        }
    }

    /// <summary>
    /// Switches to the follow camera controller
    /// </summary>
    private void SwitchToFollowCameraController() {
        if (CameraController != followCameraController) {
            CameraController = followCameraController;
            Logger.Information("Switched to follow camera controller");
        }
    }

    /// <summary>
    /// Gets the current controller type name for UI display
    /// </summary>
    public string GetCurrentControllerType() {
        return CameraController switch {
            FreeCameraController => "Free Camera",
            FollowCameraController => "Follow Camera",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// Starts following a player and automatically switches to follow camera controller
    /// </summary>
    public void StartFollowingPlayer(FieldPlayer player) {
        HasManuallyStoppedFollowing = false; // Reset manual stop flag when starting to follow
        SwitchToFollowCameraController();
        followCameraController.StartFollowingPlayer(player.Value.Character.Id, player.Position);
    }

    /// <summary>
    /// Stops following a player and automatically switches to free camera controller
    /// </summary>
    public void StopFollowingPlayer() {
        HasManuallyStoppedFollowing = true; // Remember that user manually stopped
        followCameraController.StopFollowingPlayer();
        SwitchToFreeCameraController();
    }

    /// <summary>
    /// Updates player follow position (only works when follow controller is active)
    /// </summary>
    public void UpdatePlayerFollow(Vector3 playerPosition) {
        if (CameraController == followCameraController) {
            followCameraController.UpdatePlayerFollow(playerPosition);
        }
    }

    private void UpdateWireframeInstance(DebugFieldWindow window) {
        window.InstanceConstantBuffer.Update(in instanceBuffer);

        window.SceneState.BindConstantBuffer(window.InstanceConstantBuffer, 1, Enum.ShaderStageFlags.Vertex);
        window.SceneState.UpdateBindings();
    }

    private void RenderTriggerBoxes(DebugFieldWindow window) {
        if (Field.TriggerObjects.Boxes.Count == 0) return;
        instanceBuffer.Color = new Vector4(1.0f, 0.3f, 0.8f, 0.9f); // magenta-ish
        foreach (TriggerBox box in Field.TriggerObjects.Boxes.Values) {
            Vector3 size = box.Metadata.Dimensions;
            Vector3 center = box.Metadata.Position;
            instanceBuffer.Transformation = Matrix4x4.Transpose(Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(center));
            UpdateWireframeInstance(window);
            Context.CoreModels!.WireCube.Draw();
        }
    }

    private void RenderTriggerTextLabels() {
        if (Field.TriggerObjects.Boxes.Count == 0) return;
        ImDrawListPtr drawList = ImGui.GetBackgroundDrawList();
        foreach (TriggerBox box in Field.TriggerObjects.Boxes.Values) {
            Vector3 textPos = box.Metadata.Position + new Vector3(0, 0, 150);
            if (!TryWorldToScreen(textPos, out Vector2 screenPos)) continue;
            string idText = $"TriggerBox Id: {box.Id}";
            Vector2 textSize = ImGui.CalcTextSize(idText);
            float left = screenPos.X - (textSize.X * 0.5f);
            Vector2 topLeft = new(left, screenPos.Y);
            Vector4 textColor = new(1.0f, 0.3f, 0.8f, 1.0f);
            Vector4 bgColor = new(0, 0, 0, 0.75f);
            Vector4 border = new(0.3f, 0.3f, 0.3f, 0.9f);
            Vector2 bgMin = topLeft - new Vector2(4, 2);
            Vector2 bgMax = topLeft + textSize + new Vector2(4, 2);
            drawList.AddRectFilled(bgMin, bgMax, ImGui.ColorConvertFloat4ToU32(bgColor));
            drawList.AddRect(bgMin, bgMax, ImGui.ColorConvertFloat4ToU32(border));
            drawList.AddText(topLeft, ImGui.ColorConvertFloat4ToU32(textColor), idText);
        }
    }

    private void RenderSkillHitboxes(DebugFieldWindow window) {
        // Set orange color for skill hitboxes
        instanceBuffer.Color = new Vector4(1.0f, 0.5f, 0.0f, 0.7f);

        // Render hitboxes for all actors with active skills
        foreach ((int _, FieldPlayer player) in Field.Players) {
            RenderActorSkillHitbox(window, player);
        }
        foreach ((int _, FieldNpc npc) in Field.Npcs) {
            RenderActorSkillHitbox(window, npc);
        }
        foreach ((int _, FieldNpc mob) in Field.Mobs) {
            RenderActorSkillHitbox(window, mob);
        }
    }

    private void RenderActorSkillHitbox(DebugFieldWindow window, IActor actor) {
        // Get the active SkillRecord from the appropriate source
        SkillRecord? skillRecord = null;

        if (actor is FieldNpc npc) {
            // For NPCs/mobs: get from MovementState's active cast task
            if (npc.MovementState.CastTask is MovementState.NpcSkillCastTask skillTask) {
                skillRecord = skillTask.Cast;
            }
        } else {
            // For players: use ActiveSkills but only if currently in a skill animation
            if (actor.Animation.Current is { Type: AnimationType.Skill }) {
                skillRecord = actor.ActiveSkills.GetMostRecent();
            }
        }

        if (skillRecord == null) {
            return;
        }

        // Only render the CURRENT attack point (not all attacks in the motion)
        // Bounds check to avoid race condition with game thread updating MotionPoint/AttackPoint
        if (skillRecord.MotionPoint >= skillRecord.Metadata.Data.Motions.Length ||
            skillRecord.AttackPoint >= skillRecord.Motion.Attacks.Length) {
            return;
        }
        SkillMetadataAttack attack = skillRecord.Attack;
        Prism prism = SkillUtils.GetPrism(attack.Range, actor.Position, actor.Rotation.Z);

        RenderPrism(window, prism, attack.Range);
    }

    private void RenderPrism(DebugFieldWindow window, Prism prism, SkillMetadataRange range) {
        IPolygon polygon = prism.Polygon;

        if (polygon is Circle circle) {
            // Render cylinder for circular hitbox
            Vector3 center = new(circle.Origin.X, circle.Origin.Y, prism.Height.Min + (prism.Height.Max - prism.Height.Min) * 0.5f);
            float radius = circle.Radius;
            float height = prism.Height.Max - prism.Height.Min;

            var rotation = Matrix4x4.CreateRotationX((float) (Math.PI / 2));
            instanceBuffer.Transformation = Matrix4x4.Transpose(
                Matrix4x4.CreateScale(new Vector3(radius, height * 0.5f, radius)) *
                rotation *
                Matrix4x4.CreateTranslation(center));

            UpdateWireframeInstance(window);
            Context.CoreModels!.Cylinder.Draw();
        }
        else if (polygon is Trapezoid trapezoid) {
            // For trapezoid, calculate center and size from the points array
            Vector2 p0 = trapezoid.Points[0];
            Vector2 p1 = trapezoid.Points[1];
            Vector2 p2 = trapezoid.Points[2];
            Vector2 p3 = trapezoid.Points[3];

            // Calculate center of the trapezoid
            Vector2 center2D = (p0 + p1 + p2 + p3) * 0.25f;
            Vector3 center = new(center2D.X, center2D.Y, prism.Height.Min + (prism.Height.Max - prism.Height.Min) * 0.5f);

            // Use range metadata for dimensions
            float avgWidth = (range.Width + range.EndWidth) * 0.5f;
            float distance = range.Distance;
            float height = prism.Height.Max - prism.Height.Min;

            // Calculate rotation from trapezoid points
            Vector2 forward2D = Vector2.Normalize(p2 - p1);
            float angleRad = MathF.Atan2(forward2D.Y, forward2D.X) - MathF.PI / 2; // Adjust for coordinate system

            var rotation = Matrix4x4.CreateRotationZ(angleRad);
            instanceBuffer.Transformation = Matrix4x4.Transpose(
                Matrix4x4.CreateScale(new Vector3(avgWidth, distance, height)) *
                rotation *
                Matrix4x4.CreateTranslation(center));

            UpdateWireframeInstance(window);
            Context.CoreModels!.WireCube.Draw();
        }
        else {
            // For other polygon types, render a simple box at the prism position
            // This is a fallback - extend as needed for other shapes
            Vector3 center = new(0, 0, prism.Height.Min + (prism.Height.Max - prism.Height.Min) * 0.5f);
            float height = prism.Height.Max - prism.Height.Min;

            instanceBuffer.Transformation = Matrix4x4.Transpose(
                Matrix4x4.CreateScale(new Vector3(100, 100, height)) *
                Matrix4x4.CreateTranslation(center));

            UpdateWireframeInstance(window);
            Context.CoreModels!.WireCube.Draw();
        }
    }
}
