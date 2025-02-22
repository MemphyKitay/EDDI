﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Utilities;

namespace EddiDataDefinitions
{
    public class CommodityMarketQuote
    {
        // should ideally be readonly but we need to set it during JSON parsing
        public CommodityDefinition definition { get; private set; }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            if (definition == null)
            {
                string _name = null;
                if (additionalJsonData != null)
                {
                    if (additionalJsonData.ContainsKey("EDName"))
                    {
                        _name = (string)additionalJsonData?["EDName"];
                    }
                    if (_name == null && (additionalJsonData?.ContainsKey("name") ?? false))
                    {
                        _name = (string)additionalJsonData?["name"];
                    }
                }
                if (_name != null)
                {
                    definition = CommodityDefinition.FromNameOrEDName(_name);
                }
            }
            additionalJsonData = null;
        }

        [JsonExtensionData]
        private IDictionary<string, JToken> additionalJsonData;

        public string invariantName
        {
            get => definition?.invariantName;
        }

        public string localizedName
        {
            get => definition?.localizedName;
        }

        [PublicAPI, Obsolete("deprecated for UI usage but retained for JSON conversion from the cAPI")]
        public string name
        {
            get => definition?.localizedName;
            set
            {
                if (this.definition == null)
                {
                    CommodityDefinition newDef = CommodityDefinition.FromNameOrEDName(value);
                    this.definition = newDef;
                }
            }
        }

        // Per-station information (prices are usually integers but not always)

        [PublicAPI]
        public decimal buyprice { get; set; }

        [PublicAPI]
        public int stock { get; set; }

        // StockBracket can contain the values 0, 1, 2, 3 or "" (yes, really) so we use an optional enum

        public CommodityBracket? stockbracket { get; set; }

        [PublicAPI]
        public decimal sellprice { get; set; }

        [PublicAPI]
        public int demand { get; set; }

        // DemandBracket can contain the values 0, 1, 2, 3 or "" (yes, really) so we use an optional enum

        public CommodityBracket? demandbracket { get; set; }

        public long? EliteID => definition?.EliteID;

        [PublicAPI]
        public long? EDDBID => definition?.EDDBID;

        [PublicAPI, Obsolete("Please use localizedName or InvariantName")]
        public string category => definition?.Category.localizedName;

        // Update the definition with the new galactic average price whenever this is set.
        // Fleet carriers return zero and do not display the true average price. We must disregard that information so preserve the true average price.
        // The average pricing data is the only data which may reference our internal definition, and even then only to obtain an average price.
        [PublicAPI]
        public decimal avgprice
        {
            get => definition?.avgprice ?? 0;
            set
            {
                if (definition is null)
                {
                    return;
                }
                definition.avgprice = value;
            }
        }

        [PublicAPI]
        public bool rare => definition?.rare ?? false;
        
        public HashSet<string> StatusFlags { get; set; }

        [JsonConstructor]
        public CommodityMarketQuote(CommodityDefinition definition)
        {
            if (definition is null) { return; }
            this.definition = definition;
        }
    }
}
