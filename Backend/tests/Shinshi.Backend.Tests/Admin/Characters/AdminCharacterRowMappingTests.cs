// System
using System;
using System.Data;

// Third Party
using Dapper;
using Xunit;

// Project
using Shinshi.Contracts.Admin;

namespace Shinshi.Backend.Tests.Admin
{
    /// <summary> 고정 SELECT의 별칭을 읽기 전용 DataContract DTO private 필드에 매핑할 수 있는지 DB 없이 검사한다 </summary>
    public sealed class AdminCharacterRowMappingTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    ATTACK            = 10;
        private const int    HEALTH            = 20;
        private const int    DEFENSE           = 3;
        private const int    CRIT              = 4;
        private const int    ENERGY            = 5;
        private const int    ITEM_CAP          = 40;
        private const int    TOTAL_CAP         = 100;
        private const int    SLOT              = 2;
        private const int    GRADE             = 3;
        private const int    ENHANCEMENT       = 12;
        private const long   CURRENCY          = 1234;
        private const long   CURRENCY_RESERVED = 50;
        private const long   TICKETS           = 8;
        private const long   TICKETS_RESERVED  = 2;
        private const long   ACTION            = 18;
        private const long   REVISION          = 13;
        private const long   TIME_UNIX_MS      = 1767323045000;
        private const string ITEM_ID          = "1ecab1ca-8bdb-4f8b-8028-6985b024f357";
        private const string DEFINITION_ID    = "equip_mapping";

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public void GetRowParser_TrainingColumns_PreservesEveryInvestmentAndCap()
        {
            using DataTable table = Table(
                ("Attack", ATTACK), ("Health", HEALTH), ("Defense", DEFENSE), ("Crit", CRIT), ("Energy", ENERGY),
                ("Total", ATTACK + HEALTH + DEFENSE + CRIT + ENERGY), ("ItemCap", ITEM_CAP), ("TotalCap", TOTAL_CAP));
            using DataTableReader reader = table.CreateDataReader();
            var parser = reader.GetRowParser<AdminCharacterTrainingView>();
            Assert.True(reader.Read());

            AdminCharacterTrainingView mapped = parser(reader);

            Assert.Equal(ATTACK, mapped.Attack);
            Assert.Equal(HEALTH, mapped.Health);
            Assert.Equal(DEFENSE, mapped.Defense);
            Assert.Equal(CRIT, mapped.Crit);
            Assert.Equal(ENERGY, mapped.Energy);
            Assert.Equal(ATTACK + HEALTH + DEFENSE + CRIT + ENERGY, mapped.Total);
            Assert.Equal(ITEM_CAP, mapped.ItemCap);
            Assert.Equal(TOTAL_CAP, mapped.TotalCap);
        }

        [Fact]
        public void GetRowParser_EquipmentColumns_PreservesIdsFlagsAndCreationTime()
        {
            using DataTable table = Table(
                ("ItemId", ITEM_ID), ("DefinitionId", DEFINITION_ID), ("Slot", SLOT), ("Grade", GRADE),
                ("IsTraining", false), ("Origin", "Craft"), ("Enhancement", ENHANCEMENT), ("Equipped", true),
                ("Locked", true), ("Bound", true), ("CreatedAtUnixMs", TIME_UNIX_MS));
            using DataTableReader reader = table.CreateDataReader();
            var parser = reader.GetRowParser<AdminCharacterEquipmentView>();
            Assert.True(reader.Read());

            AdminCharacterEquipmentView mapped = parser(reader);

            Assert.Equal(ITEM_ID, mapped.ItemId);
            Assert.Equal(DEFINITION_ID, mapped.DefinitionId);
            Assert.Equal(SLOT, mapped.Slot);
            Assert.Equal(GRADE, mapped.Grade);
            Assert.False(mapped.IsTraining);
            Assert.Equal("Craft", mapped.Origin);
            Assert.Equal(ENHANCEMENT, mapped.Enhancement);
            Assert.True(mapped.Equipped);
            Assert.True(mapped.Locked);
            Assert.True(mapped.Bound);
            Assert.Equal(TIME_UNIX_MS, mapped.CreatedAtUnixMs);
        }

        [Fact]
        public void GetRowParser_WalletColumns_PreservesReservedAndStoredActionValues()
        {
            using DataTable table = Table(
                ("Currency", CURRENCY), ("CurrencyReserved", CURRENCY_RESERVED), ("Tickets", TICKETS),
                ("TicketsReserved", TICKETS_RESERVED), ("ActionValue", ACTION), ("ActionAnchorUnixMs", TIME_UNIX_MS),
                ("Revision", REVISION));
            using DataTableReader reader = table.CreateDataReader();
            var parser = reader.GetRowParser<AdminCharacterWalletView>();
            Assert.True(reader.Read());

            AdminCharacterWalletView mapped = parser(reader);

            Assert.Equal(CURRENCY, mapped.Currency);
            Assert.Equal(CURRENCY_RESERVED, mapped.CurrencyReserved);
            Assert.Equal(TICKETS, mapped.Tickets);
            Assert.Equal(TICKETS_RESERVED, mapped.TicketsReserved);
            Assert.Equal(ACTION, mapped.ActionValue);
            Assert.Equal(TIME_UNIX_MS, mapped.ActionAnchorUnixMs);
            Assert.Equal(REVISION, mapped.Revision);
        }

        private static DataTable Table(params (string Name, object Value)[] values)
        {
            DataTable table = new DataTable();
            foreach ((string name, object value) in values)
            {
                table.Columns.Add(name, value.GetType());
            }

            DataRow row = table.NewRow();
            foreach ((string name, object value) in values)
            {
                row[name] = value;
            }
            table.Rows.Add(row);
            return table;
        }
    }
}
