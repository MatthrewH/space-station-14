using Content.Shared.Damage;

namespace Content.Server.Destructible.Thresholds.Triggers
{
    /// <summary>
    ///     A trigger that will activate when the amount of damage received
    ///     is above the specified threshold.
    /// </summary>
    [Serializable]
    [DataDefinition]
    public sealed partial class DamageTrigger : IThresholdTrigger
    {
        /// <summary>
        ///     The amount of damage at which this threshold will trigger.
        /// </summary>
        [DataField("damage", required: true)]
        public int Damage { get; set; } = default!;

        [DataField]
        public bool Repeatable = false;

        public bool Reached(DestructibleSystem system, DamageSpecifier totalDamage, bool isPositive, DamageSpecifier? deltaDamage, EntityUid? origin = null)
        {
            if (!Repeatable)
                return totalDamage.GetTotal() >= Damage;

            if (deltaDamage == null)
                return false;

            return deltaDamage.GetTotal() >= Damage;
        }
    }
}
