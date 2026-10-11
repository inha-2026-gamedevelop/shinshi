// System
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Growth;

namespace Shinshi.Data.Sessions
{
    internal static class CharacterExperienceWriter
    {

        /****************************************
        *                Methods
        ****************************************/
        public static async Task<bool> ApplyAsync(DbConnection connection, DbTransaction transaction, Guid sessionId,
            string mode, string mapId, string? settingsJson, IReadOnlyList<SessionEventInput> events,
            IReadOnlyDictionary<Guid, ParticipantRow> participants, CancellationToken cancellationToken)
        {
            if (!events.Any(entry => entry.EventKind == GameSessionCodes.EventKind.EXPERIENCE_GRANTED))
            {
                return true;
            }

            CharacterLevelSettings? settings = string.IsNullOrEmpty(settingsJson) ? null
                : Newtonsoft.Json.JsonConvert.DeserializeObject<CharacterLevelSettings>(settingsJson);
            if ((settings == null) || !settings.IsValid() || (mode != GameSessionStates.MODE_NORMAL)
                || (settings.GetGrant(mapId, CharacterExperienceSource.NORMAL_ENEMY) == 0))
            {
                return false;
            }

            string hash = CharacterLevelRules.SettingsHash(settings);
            Guid[] characters = events.Where(entry => entry.EventKind == GameSessionCodes.EventKind.EXPERIENCE_GRANTED)
                .Select(entry => participants[entry.ParticipantId!.Value].CharacterId).Distinct().OrderBy(id => id).ToArray();
            Dictionary<Guid, long> balances = (await connection.QueryAsync<BalanceRow>(new CommandDefinition(
                """
                SELECT character_id AS "CharacterId", total_experience AS "TotalExperience"
                FROM characters WHERE character_id = ANY(@Characters) ORDER BY character_id FOR UPDATE;
                """, new { Characters = characters }, transaction, cancellationToken: cancellationToken)))
                .ToDictionary(row => row.CharacterId, row => row.TotalExperience);
            HashSet<Guid> departed = participants.Values.Where(row => row.DepartedAt.HasValue)
                .Select(row => row.ParticipantId).ToHashSet();
            HashSet<(Guid, int, string)> sources = new HashSet<(Guid, int, string)>();
            foreach (SessionEventInput entry in events)
            {
                if (entry.EventKind == GameSessionCodes.EventKind.PARTICIPANT_DEPARTED)
                {
                    departed.Add(entry.ParticipantId!.Value);
                    continue;
                }
                if (entry.EventKind != GameSessionCodes.EventKind.EXPERIENCE_GRANTED)
                {
                    continue;
                }

                Guid participantId = entry.ParticipantId!.Value;
                Guid characterId = participants[participantId].CharacterId;
                if (departed.Contains(participantId) || (entry.ExperienceSettingsHash != hash)
                    || (entry.Amount != settings.GetGrant(mapId, entry.ExperienceSourceKind))
                    || !sources.Add((participantId, entry.ExperienceSourceKind == CharacterExperienceSource.ELITE_ENEMY
                        ? CharacterExperienceSource.NORMAL_ENEMY : entry.ExperienceSourceKind, entry.ExperienceSourceId)))
                {
                    return false;
                }

                long before = balances[characterId];
                if (before > CharacterLevelRules.RequiredExperience(settings, settings.MaxLevel))
                {
                    return false;
                }
                long after = CharacterLevelRules.Add(settings, before, entry.Amount);
                int inserted = await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO character_experience_events
                        (event_id, session_id, event_seq, participant_id, character_id, source_kind, source_id,
                            requested_amount, amount, balance_before, balance_after)
                    VALUES (@EventId, @SessionId, @EventSeq, @ParticipantId, @CharacterId, @SourceKind, @SourceId,
                        @Requested, @Amount, @Before, @After)
                    ON CONFLICT DO NOTHING;
                    """, new
                    {
                        entry.EventId, SessionId = sessionId, entry.EventSeq, ParticipantId = participantId,
                        CharacterId = characterId, SourceKind = entry.ExperienceSourceKind, SourceId = entry.ExperienceSourceId,
                        Requested = entry.Amount, Amount = after - before, Before = before, After = after
                    }, transaction, cancellationToken: cancellationToken));
                if (inserted != 1)
                {
                    return false;
                }

                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE characters SET total_experience = @Total, last_experience_session_id = @SessionId,
                        last_experience_event_seq = @Seq WHERE character_id = @CharacterId;
                    """, new { Total = after, SessionId = sessionId, Seq = entry.EventSeq, CharacterId = characterId },
                    transaction, cancellationToken: cancellationToken));
                balances[characterId] = after;
            }

            return true;
        }

        private sealed class BalanceRow
        {
            public Guid CharacterId { get; set; }
            public long TotalExperience { get; set; }
        }
    }
}
