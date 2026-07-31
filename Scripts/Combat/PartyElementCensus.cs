using System.Collections.Generic;
using MysteryDungeon.Entities;

namespace MysteryDungeon.Combat;

// trait_catalog_v2 §4 stage 2-a: "パーティ属性census" - the shared
// same-faction party-lookup primitives that きずな/ちから/まもり/式 (and,
// per Jun's note, future party-wide traits like がんばりサポート/群れの
// 一員, which don't key off an element at all - see AnyAllyHasUniqueTrait)
// all build on. Deliberately generic (Element+TemplateKind or a bare
// trait id, self-inclusive or not) rather than 4 bespoke one-off
// functions, so a later trait can reuse these without a rework.
//
// Takes a plain actor enumerable rather than FloorController directly -
// AttackAction passes FloorController.AllActors() in normal play, but
// this keeps the census logic itself framework-free and trivially unit-
// testable with a hand-built list, no real floor required. "Party" means
// same Faction as the querying entity - faction-symmetric on purpose (an
// enemy-side trait holder would count other enemies, not the player's
// party), even though no enemy species carries a trait yet.
public static class PartyElementCensus
{
    // How many OTHER same-faction party members (from `actors`) have
    // `element` as one of their Types (Type1 or Type2) - used by きずな,
    // which the caller then caps at 3 bodies. Always excludes `self` - a
    // bond trait counts teammates, not the holder's own typing.
    public static int CountAlliesWithType(Entity self, IEnumerable<Entity> actors, Element element)
    {
        if (actors == null) return 0;

        string elementName = element.ToString();
        int count = 0;
        foreach (var actor in actors)
        {
            if (actor == self || actor.Faction != self.Faction) continue;
            if (actor.Stats.Type1 == elementName || actor.Stats.Type2 == elementName) count++;
        }
        return count;
    }

    // Does any same-faction party member (from `actors`, optionally
    // including `self`) hold a Template trait of `kind` whose OWN
    // declared Element matches `element`? A pure existence check, not a
    // count - this is what gives ちから/まもり/式 their "重複不可" (no
    // stacking even with multiple holders) for free, with no extra dedup
    // logic needed.
    public static bool AnyAllyHasTemplateTrait(Entity self, IEnumerable<Entity> actors, TraitTemplateKind kind, Element element, bool includeSelf)
    {
        if (actors == null) return false;

        foreach (var actor in actors)
        {
            if (actor.Faction != self.Faction) continue;
            if (!includeSelf && actor == self) continue;

            var trait = TraitDatabase.Get(actor.Stats.Trait);
            if (trait != null && trait.Category == TraitCategory.Template && trait.TemplateKind == kind && trait.Element == element)
                return true;
        }
        return false;
    }

    // Same existence-check shape as AnyAllyHasTemplateTrait, but for a
    // bare Unique trait id rather than an element-keyed Template family -
    // the hook がんばりサポート/群れの一員-style "does someone in my party
    // hold trait X" mechanics reuse (stage 3, not consumed yet - added
    // here now so the census layer doesn't need revisiting later).
    // How many OTHER same-faction members carry EITHER of two elements -
    // the shape じゆうのつばさ (竜 or 闇) and ふんどのつばさ (竜 or 炎) need.
    // A dual-typed ally matching both still counts once, so the count is
    // "bodies", not "matching types".
    public static int CountAlliesWithEitherType(Entity self, IEnumerable<Entity> actors, Element a, Element b)
    {
        if (actors == null) return 0;

        string an = a.ToString(), bn = b.ToString();
        int count = 0;
        foreach (var actor in actors)
        {
            if (actor == self || actor.Faction != self.Faction) continue;
            string t1 = actor.Stats.Type1, t2 = actor.Stats.Type2;
            if (t1 == an || t2 == an || t1 == bn || t2 == bn) count++;
        }
        return count;
    }

    // How many OTHER same-faction members hold `traitId` - the counting
    // sibling of AnyAllyHasUniqueTrait, for the リーダー traits, which scale
    // "per むれのいちいん holder" rather than checking mere existence.
    public static int CountAlliesWithUniqueTrait(Entity self, IEnumerable<Entity> actors, string traitId)
    {
        if (actors == null) return 0;

        int count = 0;
        foreach (var actor in actors)
        {
            if (actor == self || actor.Faction != self.Faction) continue;
            if (actor.Stats.Trait == traitId) count++;
        }
        return count;
    }

    public static bool AnyAllyHasUniqueTrait(Entity self, IEnumerable<Entity> actors, string traitId, bool includeSelf)
    {
        if (actors == null) return false;

        foreach (var actor in actors)
        {
            if (actor.Faction != self.Faction) continue;
            if (!includeSelf && actor == self) continue;
            if (actor.Stats.Trait == traitId) return true;
        }
        return false;
    }
}
