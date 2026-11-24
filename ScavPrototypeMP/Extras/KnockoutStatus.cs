namespace Together;

internal struct KnockoutStatus {
    bool? lastConscious;
    bool announcedKnockout;

    internal (bool SendHealth, bool SendAlert) Update(bool conscious, bool alertsAllowed) {
        bool firstSample = !lastConscious.HasValue;
        bool changed = conscious != lastConscious.GetValueOrDefault(true);
        lastConscious = conscious;
        if (!alertsAllowed) {
            announcedKnockout = false;
        }
        if (firstSample || !changed || !alertsAllowed) {
            return (changed, false);
        }

        // Only announce a recovery if there was a knockout alert for this episode.
        bool sendAlert = !conscious || announcedKnockout;
        announcedKnockout = !conscious;
        return (true, sendAlert);
    }
}
