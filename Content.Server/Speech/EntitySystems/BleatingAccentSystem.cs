using System.Text.RegularExpressions;
using Content.Server.Speech.Components;
using Content.Shared.Body.Part;

namespace Content.Server.Speech.EntitySystems;

public sealed partial class BleatingAccentSystem : EntitySystem
{
    private static readonly Regex BleatRegex = new("([mbdlpwhrkcnytfo])([aiu])", RegexOptions.IgnoreCase);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BleatingAccentComponent, AccentGetEvent>(OnAccentGet);
        SubscribeLocalEvent<BleatingAccentComponent, BodyLimbRelayedEvent<AccentGetEvent>>((u, c, a) => OnAccentGet((u, c), ref a.Args));
        SubscribeLocalEvent<BleatingAccentComponent, BodyOrganRelayedEvent<AccentGetEvent>>((u, c, a) => OnAccentGet((u, c), ref a.Args));
    }

    private void OnAccentGet(Entity<BleatingAccentComponent> entity, ref AccentGetEvent args)
    {
        args.Message = Accentuate(args.Message);
    }

    public static string Accentuate(string message)
    {
        // Repeats the vowel in certain consonant-vowel pairs
        // So you taaaalk liiiike thiiiis
        return BleatRegex.Replace(message, "$1$2$2$2$2");
    }
}
