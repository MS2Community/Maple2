using Maple2.Database.Context;
using Maple2.File.Flat;
using Maple2.File.Flat.maplestory2library;
using Maple2.File.Flat.standardmodellibrary;
using Maple2.File.Parser.MapXBlock;
using Maple2.File.Parser.Xml.Table;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Tools.Extensions;
using static M2dXmlGenerator.FeatureLocaleFilter;

namespace Maple2.File.Ingest.Mapper;

public class MapEntityMapper : TypeMapper<MapEntity> {
    private readonly HashSet<string> xBlocks;
    private readonly XBlockParser parser;

    public MapEntityMapper(MetadataContext db, XBlockParser parser) {
        xBlocks = db.MapMetadata.Select(metadata => metadata.XBlock).ToHashSet();

        this.parser = parser;
    }

    private IEnumerable<MapEntity> ParseMap(string xblock, IEnumerable<IMapEntity> entities) {
        IMS2Bounding? firstBounding = null;
        IMS2Bounding? secondBounding = null;

        Dictionary<string, IMS2WayPoint> ms2WayPoints = new();
        IMapEntity[] mapEntities = entities as IMapEntity[] ?? entities.ToArray();
        foreach (IMapEntity wayPoint in mapEntities) {
            switch (wayPoint) {
                case IMS2WayPoint ms2WayPoint:
                    ms2WayPoints.Add(ms2WayPoint.EntityId, ms2WayPoint);
                    break;
                default:
                    break;
            }
        }

        foreach (IMapEntity entity in mapEntities) {
            switch (entity) {
                case IMS2InteractObject interactObject:

                    switch (interactObject) {
                        case IMS2InteractActor interactActor:
                            yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                                Block = new Ms2InteractActor(interactActor.interactID, interactActor.Position, interactActor.Rotation),
                            };
                            continue;
                        case IMS2InteractDisplay interactDisplay:
                            yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                                Block = new Ms2InteractDisplay(interactDisplay.interactID, interactDisplay.Position, interactDisplay.Rotation),
                            };
                            continue;
                        case IMS2InteractMesh interactMesh:
                            yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                                Block = new Ms2InteractMesh(interactMesh.interactID, interactMesh.Position, interactMesh.Rotation),
                            };
                            continue;
                        case IMS2SimpleUiObject simpleUiObject:
                            yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                                Block = new Ms2SimpleUiObject(simpleUiObject.interactID, simpleUiObject.Position, simpleUiObject.Rotation),
                            };
                            continue;
                        case IMS2Telescope telescope:
                            yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                                Block = new Ms2Telescope(telescope.interactID, telescope.Position, telescope.Rotation),
                            };
                            continue;
                    }
                    continue;
                case IPortal portal:
                    if (!FeatureEnabled(portal.feature) || !HasLocale(portal.locale)) continue;
                    yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                        Block = new Portal(portal.PortalID, portal.TargetFieldSN, portal.TargetPortalID, (PortalType) portal.PortalType, (PortalActionType) portal.ActionType, portal.Position, portal.Rotation, portal.PortalDimension, portal.frontOffset, portal.RandomDestRadius, portal.IsVisible, portal.MinimapIconVisible, portal.PortalEnable),
                    };
                    continue;
                case ISpawnPoint spawn:
                    switch (spawn) {
                        case ISpawnPointPC pcSpawn:
                            yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                                Block = new SpawnPointPC(pcSpawn.SpawnPointID, pcSpawn.Position, pcSpawn.Rotation, pcSpawn.IsVisible, pcSpawn.Enable),
                            };
                            continue;
                        case ISpawnPointNPC npcSpawn:
                            IList<SpawnPointNPCListEntry> npcList = npcSpawn.NpcList.Select(entry => {
                                if (!int.TryParse(entry.Key, out int npcId)) {
                                    return null;
                                }
                                if (!int.TryParse(entry.Value, out int npcCount)) {
                                    npcCount = 1;
                                }

                                NpcMapper.NpcMetadataById.TryGetValue(npcId, out NpcMetadata? npcMetadata);
                                if (npcMetadata == null) {
                                    return null;
                                }

                                // Some NPC spawns have these be equal, so default to 1
                                if (npcId == npcCount) {
                                    npcCount = 1;
                                }

                                return new SpawnPointNPCListEntry(npcId, npcCount);
                            }).WhereNotNull().ToList();
                            if (npcSpawn.NpcCount == 0 || npcList.Count == 0) {
                                //Console.WriteLine($"No NPCs for {xblock}:{entity.EntityId}");
                                continue;
                            }

                            switch (npcSpawn) {
                                case IEventSpawnPointNPC eventNpcSpawn:
                                    yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                                        Block = new EventSpawnPointNPC(npcSpawn.EntityId, npcSpawn.SpawnPointID, npcSpawn.Position, npcSpawn.Rotation, npcSpawn.IsVisible, npcSpawn.IsSpawnOnFieldCreate, npcSpawn.SpawnRadius, (int) npcSpawn.NpcCount, npcList, (int) npcSpawn.RegenCheckTime, (int) eventNpcSpawn.LifeTime, eventNpcSpawn.SpawnAnimation),
                                    };
                                    continue;
                                default:
                                    string? patrolData = npcSpawn.PatrolData != "00000000-0000-0000-0000-000000000000" ? npcSpawn.PatrolData.Replace("-", string.Empty) : null;
                                    yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                                        Block = new SpawnPointNPC(npcSpawn.EntityId, npcSpawn.SpawnPointID, npcSpawn.Position, npcSpawn.Rotation, npcSpawn.IsVisible, npcSpawn.IsSpawnOnFieldCreate, npcSpawn.SpawnRadius, npcList, (int) npcSpawn.RegenCheckTime, patrolData),
                                    };
                                    continue;
                            }
                        case IEventSpawnPointItem itemSpawn:
                            yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                                Block = new EventSpawnPointItem(itemSpawn.SpawnPointID, itemSpawn.Position, itemSpawn.Rotation, itemSpawn.LifeTime, int.TryParse(itemSpawn.individualDropBoxId, out int individualDropBoxId) ? individualDropBoxId : 0, int.TryParse(itemSpawn.globalDropBoxId, out int globalDropBoxId) ? globalDropBoxId : 0, (int) itemSpawn.globalDropLevel, itemSpawn.IsVisible),
                            };
                            continue;
                    }
                    continue;
                case IMS2RegionSpawnBase spawn:
                    yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                        Block = new Ms2RegionSpawn(spawn.SpawnPointID, spawn.UseRotAsSpawnDir, spawn.Position, spawn.Rotation),
                    };
                    continue;
                case IMS2TriggerObject triggerObject:
                    MapEntity? trigger = ParseTrigger(xblock, triggerObject);
                    if (trigger != null) {
                        yield return trigger;
                    }
                    continue;
                case IMS2RegionSkill skill:
                    yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                        Block = new Ms2RegionSkill(skill.skillID, (short) skill.skillLevel, skill.Interval, skill.Position, skill.Rotation),
                    };
                    continue;
                // case IMS2Breakable breakable: {
                //     switch (breakable) {
                //         case IMS2BreakableNIF nif:
                //             yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                //                 Block = new Breakable(nif.IsVisible, (int) nif.TriggerBreakableID, nif.hideTimer, nif.resetTimer, nif.Position, nif.Rotation)
                //             };
                //             continue;
                //     }
                //     continue;
                // }
                case IMS2TriggerModel triggerModel:
                    string name = Path.GetFileNameWithoutExtension(triggerModel.XmlFilePath);
                    yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                        Block = new TriggerModel(triggerModel.TriggerModelID, name, triggerModel.Position, triggerModel.Rotation),
                    };
                    continue;
                case IMS2Bounding bounding:
                    if (firstBounding == null) {
                        firstBounding = bounding;
                        continue;
                    }
                    // Map 020000118 has 3 bounding boxes. Quick fix to ignore the 3rd.
                    if (secondBounding == null) {
                        secondBounding = bounding;
                        yield return new MapEntity(xblock, new Guid(entity.EntityId), $"{firstBounding.EntityName},{bounding.EntityName}") {
                            Block = new Ms2Bounding(firstBounding.Position, bounding.Position),
                        };
                    }
                    continue;
                case IMS2LiftableTargetBox liftableTargetBox:
                    yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                        Block = new LiftableTargetBox(liftableTargetBox.Position, liftableTargetBox.Rotation, liftableTargetBox.isForceFinish, liftableTargetBox.liftableTarget),
                    };
                    continue;
                case IMS2MapProperties mapProperties:
                    switch (mapProperties) {
                        case IMS2PhysXProp physXProp:
                            if (mapProperties.IsObjectWeapon) {
                                int[] itemIds = physXProp.ObjectWeaponItemCode.Split(',').Select(int.Parse).ToArray();
                                if (physXProp.ObjectWeaponSpawnNpcCode == 0) {
                                    yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                                        Block = new ObjectWeapon(itemIds, (int) physXProp.ObjectWeaponRespawnTick, physXProp.ObjectWeaponActiveDistance, physXProp.Position, physXProp.Rotation),
                                    };
                                } else {
                                    yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                                        Block = new ObjectWeapon(itemIds, (int) physXProp.ObjectWeaponRespawnTick, physXProp.ObjectWeaponActiveDistance, physXProp.Position, physXProp.Rotation, (int) physXProp.ObjectWeaponSpawnNpcCode, (int) physXProp.ObjectWeaponSpawnNpcCount, physXProp.ObjectWeaponSpawnNpcRate, (int) physXProp.ObjectWeaponSpawnNpcLifeTick),
                                    };
                                }
                                continue;
                            }

                            switch (physXProp) {
                                case IMS2CubeProp { skillID: > 0, skillLevel: > 0 } cubeProp:
                                    yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                                        Block = new Ms2CubeSkill(cubeProp.skillID, (short) cubeProp.skillLevel, cubeProp.Position, cubeProp.Rotation),
                                    };
                                    continue;
                                case IMS2Liftable liftable:
                                    yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                                        Block = new Liftable((int) liftable.ItemID, liftable.ItemStackCount, liftable.ItemLifeTime, liftable.LiftableRegenCheckTime, liftable.LiftableFinishTime, liftable.MaskQuestID, liftable.MaskQuestState, liftable.EffectQuestID, liftable.EffectQuestState, liftable.IsReactEffect, liftable.Position, liftable.Rotation),
                                    };
                                    continue;
                                case IMS2TaxiStation taxiStation:
                                    yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                                        Block = new TaxiStation(taxiStation.Position, taxiStation.Rotation),
                                    };
                                    continue;
                                    // Intentionally do not parse IMS2Vibrate, there are 4M entries.
                                    // case IMS2Vibrate vibrate:
                            }
                            continue;
                    }
                    continue;
                case IActor actor: {
                        switch (actor) {
                            case IMS2BreakableActor breakable:
                                int.TryParse(breakable.additionGlobalDropBoxId, out int globalDropBoxId);
                                yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                                    Block = new BreakableActor(actor.IsVisible, (int) breakable.TriggerBreakableID, breakable.hideTimer, breakable.resetTimer, globalDropBoxId, breakable.Position, breakable.Rotation),
                                };
                                continue;
                        }
                        continue;
                    }
                case IMS2PatrolData patrolData:
                    List<MS2WayPoint> wayPoints = [];
                    foreach (KeyValuePair<string, string> wayPointDict in patrolData.WayPoints) {
                        IMS2WayPoint? wayPoint = ms2WayPoints.GetValueOrDefault(wayPointDict.Value.Replace("-", string.Empty));
                        if (wayPoint is null) {
                            continue;
                        }

                        patrolData.ApproachAnims.TryGetValue(wayPointDict.Key, out string? approachAnimation);
                        patrolData.ArriveAnims.TryGetValue(wayPointDict.Key, out string? arriveAnimation);
                        patrolData.ArriveAnimsTime.TryGetValue(wayPointDict.Key, out uint arriveAnimationTime);
                        wayPoints.Add(new MS2WayPoint(wayPoint.EntityId, wayPoint.IsVisible, wayPoint.Position, wayPoint.Rotation, approachAnimation ?? "", arriveAnimation ?? "", (int) arriveAnimationTime, patrolData.IsAirWayPoint));
                    }


                    yield return new MapEntity(xblock, new Guid(entity.EntityId), entity.EntityName) {
                        Block = new MS2PatrolData(patrolData.EntityId, patrolData.EntityName, patrolData.IsAirWayPoint, (int) patrolData.PatrolSpeed, patrolData.IsLoop, wayPoints),
                    };
                    continue;
            }
        }

    }

    private MapEntity? ParseTrigger(string xblock, IMS2TriggerObject trigger) {
        switch (trigger) {
            case IMS2TriggerActor actor:
                return new MapEntity(xblock, new Guid(trigger.EntityId), trigger.EntityName) {
                    Block = new Ms2TriggerActor(actor.InitialSequence, actor.TriggerObjectID, actor.IsVisible),
                };
            case IMS2TriggerAgent agent:
                return new MapEntity(xblock, new Guid(trigger.EntityId), trigger.EntityName) {
                    Block = new Ms2TriggerAgent(agent.TriggerObjectID, agent.IsVisible),
                };
            case IMS2TriggerBlock block:
                return null;
            case IMS2TriggerBox box:
                return new MapEntity(xblock, new Guid(trigger.EntityId), trigger.EntityName) {
                    Block = new Ms2TriggerBox(box.Position, box.ShapeDimensions, box.TriggerObjectID, box.IsVisible),
                };
            case IMS2TriggerCamera camera:
                return new MapEntity(xblock, new Guid(trigger.EntityId), trigger.EntityName) {
                    Block = new Ms2TriggerCamera(camera.TriggerObjectID, camera.IsVisible),
                };
            case IMS2TriggerCube cube:
                return new MapEntity(xblock, new Guid(trigger.EntityId), trigger.EntityName) {
                    Block = new Ms2TriggerCube(cube.TriggerObjectID, cube.IsVisible),
                };
            case IMS2TriggerEffect effect:
                return new MapEntity(xblock, new Guid(trigger.EntityId), trigger.EntityName) {
                    Block = new Ms2TriggerEffect(effect.TriggerObjectID, effect.IsVisible),
                };
            case IMS2TriggerLadder ladder:
                return new MapEntity(xblock, new Guid(trigger.EntityId), trigger.EntityName) {
                    Block = new Ms2TriggerLadder(ladder.TriggerObjectID, ladder.IsVisible),
                };
            case IMS2TriggerMesh mesh:
                return new MapEntity(xblock, new Guid(trigger.EntityId), trigger.EntityName) {
                    Block = new Ms2TriggerMesh(mesh.Scale, mesh.TriggerObjectID, mesh.IsVisible, mesh.MinimapInVisible),
                };
            case IMS2TriggerPortal _:
                throw new InvalidOperationException("IMS2TriggerPortal should be parsed as IPortal.");
            case IMS2TriggerRope rope:
                return new MapEntity(xblock, new Guid(trigger.EntityId), trigger.EntityName) {
                    Block = new Ms2TriggerRope(rope.TriggerObjectID, rope.IsVisible),
                };
            case IMS2TriggerSkill skill:
                if (skill.skillID <= 0 || skill.skillLevel <= 0) {
                    return null;
                }
                return new MapEntity(xblock, new Guid(trigger.EntityId), trigger.EntityName) {
                    Block = new Ms2TriggerSkill(skill.skillID, (short) skill.skillLevel, skill.Position, skill.Rotation, skill.TriggerObjectID, skill.IsVisible, skill.count),
                };
            case IMS2TriggerSound sound:
                return new MapEntity(xblock, new Guid(trigger.EntityId), trigger.EntityName) {
                    Block = new Ms2TriggerSound(sound.TriggerObjectID, sound.IsVisible),
                };
        }

        // Generic MS2TriggerObject
        return null;
    }

    protected override IEnumerable<MapEntity> Map() {
        return parser.Parallel().SelectMany(map => {
            string xblock = map.xblock.ToLower();
            if (!xBlocks.Contains(xblock)) {
                return [];
            }

            return ParseMap(xblock, map.entities);
        }) // Ordering to ensure deterministic checksums.
        .OrderBy(entity => entity.XBlock)
        .ThenBy(entity => entity.Guid);
    }
}
