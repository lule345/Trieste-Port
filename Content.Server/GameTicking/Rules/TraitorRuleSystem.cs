using Content.Server.Antag;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Mind;
using Content.Server.Objectives;
using Content.Server.PDA.Ringer;
using Content.Server.Roles;
using Content.Server.Traitor.Uplink;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mind;
using Content.Shared.NPC.Systems;
using Content.Shared.Objectives.Components;
using Content.Shared.PDA;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using System.Linq;
using System.Text;

namespace Content.Server.GameTicking.Rules;

public sealed class TraitorRuleSystem : GameRuleSystem<TraitorRuleComponent>
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly NpcFactionSystem _npcFaction = default!;
    [Dependency] private readonly AntagSelectionSystem _antag = default!;
    [Dependency] private readonly UplinkSystem _uplink = default!;
    [Dependency] private readonly MindSystem _mindSystem = default!;
    [Dependency] private readonly SharedRoleSystem _roleSystem = default!;
    [Dependency] private readonly SharedJobSystem _jobs = default!;
    [Dependency] private readonly ObjectivesSystem _objectives = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TraitorRuleComponent, AfterAntagEntitySelectedEvent>(AfterEntitySelected);
        SubscribeLocalEvent<TraitorRuleComponent, ObjectivesTextPrependEvent>(OnObjectivesTextPrepend);
    }

    protected override void Added(EntityUid uid, TraitorRuleComponent component, GameRuleComponent gameRule, GameRuleAddedEvent args)
    {
        base.Added(uid, component, gameRule, args);
        MakeCodewords(component, "Syndicate"); // Generate Syndicate codewords by default
    }

    private void AfterEntitySelected(Entity<TraitorRuleComponent> ent, ref AfterAntagEntitySelectedEvent args)
    {
        // Roll to decide the type of traitor
        var isSyndicate = _random.Prob(0.5f);

        if (isSyndicate)
        {
            MakeSyndicateTraitor(args.EntityUid, ent);
        }
        else
        {
            MakeNanoTrasenTraitor(args.EntityUid, ent);
        }
    }

    private void MakeCodewords(TraitorRuleComponent component, string type)
    {
        var adjectives = _prototypeManager.Index(component.CodewordAdjectives).Values;
        var verbs = _prototypeManager.Index(component.CodewordVerbs).Values;
        var codewordPool = adjectives.Concat(verbs).ToList();
        var finalCodewordCount = Math.Min(component.CodewordCount, codewordPool.Count);

        var codewords = new string[finalCodewordCount];
        for (var i = 0; i < finalCodewordCount; i++)
        {
            codewords[i] = _random.PickAndTake(codewordPool);
        }

        if (type == "Syndicate")
        {
            component.SyndicateCodewords = codewords;
        }
        else
        {
            component.NanoTrasenCodewords = codewords;
        }
    }

    public bool MakeSyndicateTraitor(EntityUid traitor, TraitorRuleComponent component, bool giveUplink = true)
    {
        if (!_mindSystem.TryGetMind(traitor, out var mindId, out var mind))
            return false;

        var briefing = Loc.GetString("traitor-role-codewords-short", ("codewords", string.Join(", ", component.SyndicateCodewords)));
        var issuer = _random.Pick(_prototypeManager.Index(component.ObjectiveIssuers).Values);

        Note[]? code = null;
        if (giveUplink)
        {
            var startingBalance = component.StartingBalance;
            if (_jobs.MindTryGetJob(mindId, out _, out var prototype))
                startingBalance = Math.Max(startingBalance - prototype.AntagAdvantage, 0);

            var pda = _uplink.FindUplinkTarget(traitor);
            if (pda == null || !_uplink.AddUplink(traitor, startingBalance))
                return false;

            code = EnsureComp<RingerUplinkComponent>(pda.Value).Code;
            briefing = string.Format("{0}\n{1}", briefing,
                Loc.GetString("traitor-role-uplink-code-short", ("code", string.Join("-", code).Replace("sharp", "#"))));
        }

        _antag.SendBriefing(traitor, GenerateBriefing(component.SyndicateCodewords, code, issuer), null, component.GreetSoundNotification);
        component.TraitorMinds.Add(mindId);

        _roleSystem.MindAddRole(mindId, new RoleBriefingComponent
        {
            Briefing = briefing
        }, mind, true);

        _npcFaction.RemoveFaction(traitor, component.NanoTrasenFaction, false);
        _npcFaction.AddFaction(traitor, component.SyndicateFaction);

        return true;
    }

public bool MakeNanoTrasenTraitor(EntityUid traitor, TraitorRuleComponent component, bool giveUplinkNT = true)
{
    MakeCodewords(component, "NanoTrasen");

    if (!_mindSystem.TryGetMind(traitor, out var mindId, out var mind))
        return false;

    var briefing = Loc.GetString("traitor-role-codewords-short", ("codewords", string.Join(", ", component.NanoTrasenCodewords)));
    var issuer = _random.Pick(_prototypeManager.Index(component.ObjectiveIssuers).Values);

    Note[]? code = null;
    if (giveUplinkNT)
    {
        var startingBalance = component.StartingBalance;
        if (_jobs.MindTryGetJob(mindId, out _, out var prototype))
            startingBalance = Math.Max(startingBalance - prototype.AntagAdvantage, 0);

        var headset = _uplink.FindUplinkTargetNT(traitor);
        if (headset == null)
            return false;

        // Add NanoTrasen uplink with Bluespace Crystals
        if (!_uplink.AddUplinkNT(traitor, startingBalance, headset))
            return false;

        // the code for the headset
        code = EnsureComp<RingerUplinkComponent>(headset.Value).Code;
        briefing = string.Format("{0}\n{1}", briefing,
            Loc.GetString("traitor-role-uplink-code-short", ("code", string.Join("-", code).Replace("sharp", "#"))));

        // Ensure the listener component and assign codewords
        var listener = EnsureComp<ActiveListenerComponent>(headset.Value);
        listener.Codewords = component.NanoTrasenCodewords.ToList();
    }

    _antag.SendBriefing(traitor, GenerateBriefing(component.NanoTrasenCodewords, code, issuer), null, component.GreetSoundNotification);
    component.TraitorMinds.Add(mindId);

    _roleSystem.MindAddRole(mindId, new RoleBriefingComponent
    {
        Briefing = briefing
    }, mind, true);

    _npcFaction.RemoveFaction(traitor, component.NanoTrasenFaction, false);
    _npcFaction.AddFaction(traitor, component.NanoTrasenTraitorFaction);

    return true;
}



    private void OnObjectivesTextPrepend(EntityUid uid, TraitorRuleComponent comp, ref ObjectivesTextPrependEvent args)
    {
        args.Text += "\n" + Loc.GetString("traitor-round-end-codewords", ("codewords", string.Join(", ", comp.Codewords)));
    }

    private string GenerateBriefing(string[] codewords, Note[]? uplinkCode, string? objectiveIssuer = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Loc.GetString("traitor-role-greeting", ("corporation", objectiveIssuer ?? Loc.GetString("objective-issuer-unknown"))));
        sb.AppendLine(Loc.GetString("traitor-role-codewords-short", ("codewords", string.Join(", ", codewords))));
        if (uplinkCode != null)
            sb.AppendLine(Loc.GetString("traitor-role-uplink-code-short", ("code", string.Join("-", uplinkCode).Replace("sharp", "#"))));

        return sb.ToString();
    }

    public List<(EntityUid Id, MindComponent Mind)> GetOtherTraitorMindsAliveAndConnected(MindComponent ourMind)
    {
        List<(EntityUid Id, MindComponent Mind)> allTraitors = new();

        var query = EntityQueryEnumerator<TraitorRuleComponent>();
        while (query.MoveNext(out var uid, out var traitor))
        {
            foreach (var role in GetOtherTraitorMindsAliveAndConnected(ourMind, (uid, traitor)))
            {
                if (!allTraitors.Contains(role))
                    allTraitors.Add(role);
            }
        }

        return allTraitors;
    }

    private List<(EntityUid Id, MindComponent Mind)> GetOtherTraitorMindsAliveAndConnected(MindComponent ourMind, Entity<TraitorRuleComponent> rule)
    {
        var traitors = new List<(EntityUid Id, MindComponent Mind)>();
        foreach (var mind in _antag.GetAntagMinds(rule.Owner))
        {
            if (mind.Comp == ourMind)
                continue;

            traitors.Add((mind, mind));
        }

        return traitors;
    }
}
