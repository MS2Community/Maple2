using System.Diagnostics.CodeAnalysis;
using System.Xml;
using Caching;
using Maple2.Database.Storage;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Tools;
using Serilog;
using static Maple2.Server.Game.Trigger.Helpers.Trigger;
// ReSharper disable HeuristicUnreachableCode
#pragma warning disable CS0162 // Unreachable code detected

namespace Maple2.Server.Game.Util;

public class TriggerCache : LRUCache<(string, string), Trigger.Helpers.Trigger> {
    private const int CacheSize = 5000; // ~5k total triggers
    private readonly TriggerScriptMetadata triggerMetadata;
    public TriggerCache(TriggerScriptMetadata triggerMetadata) : base(CacheSize, (int) (CacheSize * 0.05)) {
        this.triggerMetadata = triggerMetadata;
    }

    public bool TryGet(string mapXBlock, string triggerName, [NotNullWhen(true)] out Trigger.Helpers.Trigger? trigger) {
        if (TryGet((mapXBlock, triggerName), out trigger)) {
            return true;
        }

        if (!Constant.DebugTriggers) {
            if (triggerMetadata.TryGet(mapXBlock, triggerName, out TriggerMetadata? metadata)) {
                try {
                    var document = new XmlDocument();
                    document.LoadXml(metadata.Xml);
                    trigger = ParseTrigger(mapXBlock, triggerName, document);
                    AddReplace((mapXBlock, triggerName), trigger);
                    return true;
                } catch (Exception ex) {
                    Log.Error(ex, "Failed to parse trigger {TriggerName} in {MapXBlock}.", triggerName, mapXBlock);
                }
            } else {
                Log.Error("Trigger {TriggerName} not found in {MapXBlock}.", triggerName, mapXBlock);
            }
        } else {
            string triggerFilePath = Path.Combine(Paths.DEBUG_TRIGGERS_DIR, mapXBlock, triggerName + ".xml");
            if (!File.Exists(triggerFilePath)) {
                throw new ArgumentException($"You are running DebugTriggers, but the trigger file does not exist: {triggerFilePath}");
            }
            var document = new XmlDocument();
            document.LoadXml(File.ReadAllText(triggerFilePath));
            trigger = ParseTrigger(mapXBlock, triggerName, document);
            // dont add to cache in debug mode
            return true;
        }

        trigger = null;
        return false;
    }

    private Trigger.Helpers.Trigger ParseTrigger(string xBlock, string triggerName, XmlDocument doc) {
        XmlElement? root = doc.DocumentElement;
        if (root is null || root.Name != "ms2") {
            throw new ArgumentException("Trigger XML must have a root element named <ms2>.");
        }
        XmlNodeList? stateNodes = root.SelectNodes("state");
        if (stateNodes is null) {
            throw new ArgumentException("Trigger XML must contain at least one <state> element.");
        }

        List<State> states = [];
        foreach (XmlNode stateNode in stateNodes) {
            if (stateNode is not XmlElement stateElement || !stateElement.HasAttribute("name")) {
                continue;
            }

            string stateName = stateElement.GetAttribute("name");

            State.OnEnter? onEnter = null;
            XmlNode? onEnterNode = stateElement.SelectSingleNode("onEnter");
            if (onEnterNode != null) {
                LinkedList<IAction> onEnterActions = ParseActions(onEnterNode, stateName, triggerName, xBlock, "onEnter");

                XmlNode? transitionNode = onEnterNode.SelectSingleNode("transition");

                string? nextStateName = null;
                if (transitionNode is { Attributes: not null }) {
                    nextStateName = transitionNode.Attributes["state"]?.Value;
                }

                onEnter = new State.OnEnter(onEnterActions, nextStateName);
            }

            LinkedList<ICondition> conditions = [];
            XmlNodeList? conditionNodes = stateElement.SelectNodes("condition");
            if (conditionNodes != null) {
                foreach (XmlNode conditionXmlNode in conditionNodes) {
                    XmlNode conditionNode = conditionXmlNode;
                    string? conditionName = conditionNode.Attributes?["name"]?.Value;
                    if (conditionName is null) {
                        Log.Error("Condition in trigger state '{StateName}' for trigger '{TriggerName}' in {MapXBlock} must have a 'name' attribute.", stateName, triggerName, xBlock);
                        continue;
                    }
                    TriggerFunctionMapping.ConditionMap.TryGetValue(conditionName, out Func<XmlAttributeCollection?, ICondition>? conditionMap);
                    if (conditionMap == null) {
                        Log.Error("Unknown condition type '{ConditionType}' in trigger state '{StateName}' for trigger '{TriggerName}' in {MapXBlock}", conditionName, stateName, triggerName, xBlock);
                        continue;
                    }

                    ICondition condition = conditionMap(conditionNode.Attributes);
                    if (condition is GroupAnyOne or GroupAllOf) {
                        XmlNode? groupNode = conditionNode.SelectSingleNode("group");
                        if (groupNode is null) {
                            Log.Error("Group condition in trigger state '{StateName}' for trigger '{TriggerName}' must have a <group> element in {MapXBlock}", stateName, triggerName, xBlock);
                            continue;
                        }

                        conditionNode = groupNode;
                    }

                    XmlNode? transitionNode = conditionNode.SelectSingleNode("transition");
                    if (transitionNode is { Attributes: not null }) {
                        string? nextStateName = transitionNode.Attributes["state"]?.Value;
                        if (nextStateName is not null) {
                            condition.NextState = nextStateName;
                        }
                    }

                    LinkedList<IAction> actions = ParseActions(conditionNode, stateName, triggerName, xBlock, "condition");
                    condition.Actions = actions;
                    conditions.AddLast(condition);
                }
            }

            State.OnExit? onExit = null;
            XmlNode? onExitNode = stateElement.SelectSingleNode("onExit");
            if (onExitNode != null) {
                LinkedList<IAction> onExitActions = ParseActions(onExitNode, stateName, triggerName, xBlock, "onExit");

                onExit = new State.OnExit(onExitActions);
            }

            states.Add(new State(stateName, conditions, onEnter, onExit));
        }

        return new Trigger.Helpers.Trigger(states);
    }

    private LinkedList<IAction> ParseActions(XmlNode parentNode, string stateName, string triggerName, string xBlock, string context) {
        LinkedList<IAction> actions = [];
        foreach (XmlNode actionNode in parentNode.SelectNodes("action")!) {
            string? actionName = actionNode.Attributes?["name"]?.Value;
            if (actionName is null) {
                Log.Error("Action in {Context} of trigger state '{StateName}' for trigger '{TriggerName}' in {MapXBlock} must have a 'name' attribute.", context, stateName, triggerName, xBlock);
                continue;
            }
            TriggerFunctionMapping.ActionMap.TryGetValue(actionName, out Func<XmlAttributeCollection?, IAction>? actionMap);
            if (actionMap == null) {
                Log.Error("Unknown action type '{ActionType}' in {Context} of trigger state '{StateName}' for trigger '{TriggerName}' in {MapXBlock}", actionName, context, stateName, triggerName, xBlock);
                continue;
            }
            IAction actionClass = actionMap(actionNode.Attributes);
            actions.AddLast(actionClass);
        }
        return actions;
    }
}
