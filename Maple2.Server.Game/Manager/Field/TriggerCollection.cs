using System.Collections;
using System.Collections.Generic;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Manager.Field;

public sealed class TriggerCollection : IReadOnlyCollection<ITriggerObject> {
    // NOTE: These collections need to be mutable at runtime.
    // Some map packs / DB imports are missing Ms2TriggerMesh / Ms2TriggerAgent entries,
    // but trigger scripts still reference those IDs (set_mesh / set_agent).
    // The client already knows the objects by triggerId (from the map file), and only
    // needs server updates to toggle visibility/collision.
    //
    // We therefore support creating lightweight placeholder trigger objects on demand.
    private readonly Dictionary<int, TriggerObjectActor> actors;
    private readonly Dictionary<int, TriggerObjectCamera> cameras;
    private readonly Dictionary<int, TriggerObjectCube> cubes;
    private readonly Dictionary<int, TriggerObjectEffect> effects;
    private readonly Dictionary<int, TriggerObjectLadder> ladders;
    private readonly Dictionary<int, TriggerObjectMesh> meshes;
    private readonly Dictionary<int, TriggerObjectRope> ropes;
    private readonly Dictionary<int, TriggerObjectSound> sounds;
    private readonly Dictionary<int, TriggerObjectAgent> agents;
    private readonly Dictionary<int, TriggerBox> boxes;

    public IReadOnlyDictionary<int, TriggerObjectActor> Actors => actors;
    public IReadOnlyDictionary<int, TriggerObjectCamera> Cameras => cameras;
    public IReadOnlyDictionary<int, TriggerObjectCube> Cubes => cubes;
    public IReadOnlyDictionary<int, TriggerObjectEffect> Effects => effects;
    public IReadOnlyDictionary<int, TriggerObjectLadder> Ladders => ladders;
    public IReadOnlyDictionary<int, TriggerObjectMesh> Meshes => meshes;
    public IReadOnlyDictionary<int, TriggerObjectRope> Ropes => ropes;
    public IReadOnlyDictionary<int, TriggerObjectSound> Sounds => sounds;
    public IReadOnlyDictionary<int, TriggerObjectAgent> Agents => agents;

    public IReadOnlyDictionary<int, TriggerBox> Boxes => boxes;

    // These seem to get managed separately...
    // private readonly IReadOnlyDictionary<int, TriggerObjectAgent> Agents;
    // private readonly IReadOnlyDictionary<int, TriggerObjectSkill> Skills;

    public TriggerCollection(MapEntityMetadata entities) {
        actors = new();
        cameras = new();
        cubes = new();
        effects = new();
        ladders = new();
        meshes = new();
        ropes = new();
        sounds = new();
        agents = new();

        foreach (Ms2TriggerActor actor in entities.Trigger.Actors) {
            actors[actor.TriggerId] = new TriggerObjectActor(actor);
        }
        foreach (Ms2TriggerCamera camera in entities.Trigger.Cameras) {
            cameras[camera.TriggerId] = new TriggerObjectCamera(camera);
        }
        foreach (Ms2TriggerCube cube in entities.Trigger.Cubes) {
            cubes[cube.TriggerId] = new TriggerObjectCube(cube);
        }
        foreach (Ms2TriggerEffect effect in entities.Trigger.Effects) {
            effects[effect.TriggerId] = new TriggerObjectEffect(effect);
        }
        foreach (Ms2TriggerLadder ladder in entities.Trigger.Ladders) {
            ladders[ladder.TriggerId] = new TriggerObjectLadder(ladder);
        }
        foreach (Ms2TriggerMesh mesh in entities.Trigger.Meshes) {
            meshes[mesh.TriggerId] = new TriggerObjectMesh(mesh);
        }
        foreach (Ms2TriggerRope rope in entities.Trigger.Ropes) {
            ropes[rope.TriggerId] = new TriggerObjectRope(rope);
        }
        foreach (Ms2TriggerSound camera in entities.Trigger.Sounds) {
            sounds[camera.TriggerId] = new TriggerObjectSound(camera);
        }

        foreach (Ms2TriggerAgent agent in entities.Trigger.Agents) {
            agents[agent.TriggerId] = new TriggerObjectAgent(agent);
        }

        boxes = new();
        foreach (Ms2TriggerBox box in entities.Trigger.Boxes) {
            boxes[box.TriggerId] = new TriggerBox(box);
        }
    }

    /// <summary>
    /// Creates or retrieves a Trigger Mesh placeholder. Useful when a trigger script references
    /// a mesh id that is missing from the DB import.
    /// </summary>
    public TriggerObjectMesh GetOrAddMesh(int triggerId) {
        if (meshes.TryGetValue(triggerId, out TriggerObjectMesh? mesh)) {
            return mesh;
        }

        // Scale/minimapInvisible are not important for updates; the client already has the actual mesh.
        Ms2TriggerMesh meta = new Ms2TriggerMesh(1f, triggerId, Visible: true, MinimapInvisible: false);
        mesh = new TriggerObjectMesh(meta);
        meshes[triggerId] = mesh;
        return mesh;
    }

    /// <summary>
    /// Creates or retrieves a Trigger Agent placeholder. Useful when a trigger script references
    /// an agent id that is missing from the DB import.
    /// </summary>
    public TriggerObjectAgent GetOrAddAgent(int triggerId) {
        if (agents.TryGetValue(triggerId, out TriggerObjectAgent? agent)) {
            return agent;
        }

        Ms2TriggerAgent meta = new Ms2TriggerAgent(triggerId, Visible: true);
        agent = new TriggerObjectAgent(meta);
        agents[triggerId] = agent;
        return agent;
    }

    public int Count => actors.Count + cameras.Count + cubes.Count + effects.Count + ladders.Count + meshes.Count + ropes.Count + sounds.Count + agents.Count;

    public IEnumerator<ITriggerObject> GetEnumerator() {
        foreach (TriggerObjectActor actor in actors.Values) yield return actor;
        foreach (TriggerObjectCamera camera in cameras.Values) yield return camera;
        foreach (TriggerObjectCube cube in cubes.Values) yield return cube;
        foreach (TriggerObjectEffect effect in effects.Values) yield return effect;
        foreach (TriggerObjectLadder ladder in ladders.Values) yield return ladder;
        foreach (TriggerObjectMesh mesh in meshes.Values) yield return mesh;
        foreach (TriggerObjectRope rope in ropes.Values) yield return rope;
        foreach (TriggerObjectSound sound in sounds.Values) yield return sound;
        foreach (TriggerObjectAgent agent in agents.Values) yield return agent;
    }

    IEnumerator IEnumerable.GetEnumerator() {
        return GetEnumerator();
    }
}
