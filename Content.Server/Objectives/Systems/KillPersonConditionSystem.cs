using Content.Server.GameTicking.Rules;
using Content.Server.Objectives.Components;
using Content.Server.Revolutionary.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Roles;
using Content.Shared.CCVar;
using Content.Shared.Mind;
using Content.Shared.Objectives.Components;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using System.Linq;

namespace Content.Server.Objectives.Systems;

/// <summary>
/// Handles kill person condition logic and picking random kill targets.
/// </summary>
public sealed class KillPersonConditionSystem : EntitySystem
{
    [Dependency] private readonly EmergencyShuttleSystem _emergencyShuttle = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedJobSystem _job = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly ObsessedRuleSystem _obsession = default!;
    [Dependency] private readonly IPrototypeManager _prototypeMan = default!;
    [Dependency] private readonly TargetObjectiveSystem _target = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KillPersonConditionComponent, ObjectiveGetProgressEvent>(OnGetProgress);

        SubscribeLocalEvent<PickRandomPersonComponent, ObjectiveAssignedEvent>(OnPersonAssigned);

        SubscribeLocalEvent<PickRandomHeadComponent, ObjectiveAssignedEvent>(OnHeadAssigned);

        SubscribeLocalEvent<PickObsessionComponent, ObjectiveAssignedEvent>(OnObsessionAssigned);
        SubscribeLocalEvent<PickObsessionDepartmentComponent, ObjectiveAssignedEvent>(OnObsessionDepartmentAssigned);
        SubscribeLocalEvent<PickObsessionDepartmentComponent, ObjectiveAfterAssignEvent>(OnAfterObsessionDepartmentAssigned);
    }

    private void OnGetProgress(EntityUid uid, KillPersonConditionComponent comp, ref ObjectiveGetProgressEvent args)
    {
        if (_target.GetTarget(uid, out var target) &&
            target != null)
        {
            args.Progress = GetProgress(target.Value, comp.RequireDead);
            return;
        }

        if (!_target.GetTargets(uid, out var targets) ||
            targets == null)
        {
            return;
        }

        float currentProgress = 0f;
        foreach (var currentTarget in targets)
        {
            var progress = GetProgress(currentTarget, comp.RequireDead);

            if (progress <= 1f)
            {
                currentProgress = progress;
                break;
            }

            if (progress > currentProgress)
            {
                currentProgress = progress;
            }
        }

        args.Progress = currentProgress;
    }

    private void OnPersonAssigned(EntityUid uid, PickRandomPersonComponent comp, ref ObjectiveAssignedEvent args)
    {
        // invalid objective prototype
        if (!TryComp<TargetObjectiveComponent>(uid, out var target))
        {
            args.Cancelled = true;
            return;
        }

        // target already assigned
        if (target.Target != null)
            return;

        var allHumans = _mind.GetAliveHumans(args.MindId);

        // Can't have multiple objectives to kill the same person
        foreach (var objective in args.Mind.Objectives)
        {
            if (HasComp<KillPersonConditionComponent>(objective) && TryComp<TargetObjectiveComponent>(objective, out var kill))
            {
                allHumans.RemoveWhere(x => x.Owner == kill.Target);
            }
        }

        // no other humans to kill
        if (allHumans.Count == 0)
        {
            args.Cancelled = true;
            return;
        }

        _target.SetTarget(uid, _random.Pick(allHumans), target);
    }

    private void OnHeadAssigned(EntityUid uid, PickRandomHeadComponent comp, ref ObjectiveAssignedEvent args)
    {
        // invalid prototype
        if (!TryComp<TargetObjectiveComponent>(uid, out var target))
        {
            args.Cancelled = true;
            return;
        }

        // target already assigned
        if (target.Target != null)
            return;

        // no other humans to kill
        var allHumans = _mind.GetAliveHumans(args.MindId);
        if (allHumans.Count == 0)
        {
            args.Cancelled = true;
            return;
        }

        var allHeads = new HashSet<Entity<MindComponent>>();
        foreach (var person in allHumans)
        {
            if (TryComp<MindComponent>(person, out var mind) && mind.OwnedEntity is { } ent && HasComp<CommandStaffComponent>(ent))
                allHeads.Add(person);
        }

        if (allHeads.Count == 0)
            allHeads = allHumans; // fallback to non-head target

        _target.SetTarget(uid, _random.Pick(allHeads), target);
    }

    private void OnObsessionAssigned(EntityUid uid, PickObsessionComponent comp, ref ObjectiveAssignedEvent args)
    {
        if (!TryComp<TargetObjectiveComponent>(uid, out var target))
        {
            args.Cancelled = true;
            return;
        }

        if (target.Target != null)
            return;

        if (!_obsession.TryGetRole(args.Mind, out var roleComp))
            return;

        _obsession.PickObsession(args.MindId, roleComp, args.Mind);

        if (roleComp.Obsession == null)
            return;

        _target.SetTarget(uid, roleComp.Obsession.Value, target);
    }

    private void OnObsessionDepartmentAssigned(EntityUid uid, PickObsessionDepartmentComponent comp, ref ObjectiveAssignedEvent args)
    {
        if (!TryComp<TargetObjectiveComponent>(uid, out var target))
        {
            args.Cancelled = true;
            return;
        }

        if (target.Targets != null)
            return;

        if (!_obsession.TryGetRole(args.Mind, out var roleComp))
            return;

        _obsession.PickObsession(args.MindId, roleComp, args.Mind);

        if (roleComp.Obsession == null)
        {
            args.Cancelled = true;
            return;
        }


        if (!_job.MindTryGetJob(roleComp.Obsession, out var obsessionJob))
        {
            args.Cancelled = true;
            return;
        }

        // get the obsessions department
        DepartmentPrototype? obsessionDepartment = null;
        foreach (var department in _prototypeMan.EnumeratePrototypes<DepartmentPrototype>())
        {
            if (department.Roles.Contains(obsessionJob))
            {
                obsessionDepartment = department;
                break;
            }
        }

        if (obsessionDepartment == null)
        {
            args.Cancelled = true;
            return;
        }

        var allHumans = _mind.GetAliveHumans(roleComp.Obsession.Value);
        allHumans.Remove((args.MindId, args.Mind)); //dont want the player to have a suicide mission

        // get everyone in the obsessions department
        List<EntityUid> targets = new();
        foreach (var human in allHumans)
        {
            if (!_job.MindTryGetJob(human, out var job))
                continue;

            if (obsessionDepartment.Roles.Contains(job))
                targets.Add(human);
        }

        if (targets.Count <= 0)
        {
            args.Cancelled = true;
            return;
        }

        _target.SetTargets(uid, targets, target);
    }

    private void OnAfterObsessionDepartmentAssigned(EntityUid uid, PickObsessionDepartmentComponent comp, ref ObjectiveAfterAssignEvent args)
    {
        if (!_obsession.TryGetRole(args.Mind, out var roleComp))
            return;

        if (roleComp.Obsession == null)
            return;

        var targetName = string.Empty;
        if (TryComp<MindComponent>(roleComp.Obsession, out var mind) && mind.CharacterName != null)
        {
            targetName = mind.CharacterName;
        }

        var title = Loc.GetString(comp.Title, ("targetName", targetName));

        _metaData.SetEntityName(uid, title, args.Meta);

        if (comp.Description != null)
            _metaData.SetEntityDescription(uid, Loc.GetString(comp.Description), args.Meta);
    }

    private float GetProgress(EntityUid target, bool requireDead)
    {
        // deleted or gibbed or something, counts as dead
        if (!TryComp<MindComponent>(target, out var mind) || mind.OwnedEntity == null)
            return 1f;

        // dead is success
        if (_mind.IsCharacterDeadIc(mind))
            return 1f;

        // if the target has to be dead dead then don't check evac stuff
        if (requireDead)
            return 0f;

        // if evac is disabled then they really do have to be dead
        if (!_config.GetCVar(CCVars.EmergencyShuttleEnabled))
            return 0f;

        // target is escaping so you fail
        if (_emergencyShuttle.IsTargetEscaping(mind.OwnedEntity.Value))
            return 0f;

        // evac has left without the target, greentext since the target is afk in space with a full oxygen tank and coordinates off.
        if (_emergencyShuttle.ShuttlesLeft)
            return 1f;

        // if evac is still here and target hasn't boarded, show 50% to give you an indicator that you are doing good
        return _emergencyShuttle.EmergencyShuttleArrived ? 0.5f : 0f;
    }
}
