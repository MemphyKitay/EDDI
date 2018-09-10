﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EddiDataDefinitions
{
    /// <summary>Details for a star system</summary>
    public class StarSystem
    {
        // The ID in EDDB
        public long EDDBID { get; set; }
        public string name { get; set; }
        public long? population { get; set; }
        public string allegiance { get; set; }
        public string government { get; set; }
        public string faction { get; set; }
        public string primaryeconomy => (economies[0] ?? Economy.None).localizedName;
        public List<Economy> economies { get; set; } = new List<Economy>() { null, null };
        public State systemState { get; set; } = State.None;
        public string security { get; set; }
        public string power { get; set; }
        public string powerstate { get; set; }

        [Obsolete("Please use systemState instead")]
        public string state => (systemState ?? State.None).localizedName;

        /// <summary>X co-ordinate for this system</summary>
        public decimal? x { get; set; }
        /// <summary>Y co-ordinate for this system</summary>
        public decimal? y { get; set; }
        /// <summary>Z co-ordinate for this system</summary>
        public decimal? z { get; set; }

        /// <summary>Unique 64 bit id value for system</summary>
        public long? systemAddress { get; set; }

        /// <summary>Details of stations</summary>
        public List<Station> stations { get; set; }

        /// <summary>Summary info for stations</summary>
        [JsonIgnore]
        public List<Station> planetarystations => stations.FindAll(s => s.IsPlanetary());

        [JsonIgnore]
        public List<Station> orbitalstations => stations.FindAll(s => !s.IsPlanetary());

        /// <summary>Details of bodies (stars/planets)</summary>
        public List<Body> bodies { get; set; }

        /// <summary>Number of visits</summary>
        public int visits;

        /// <summary>Time of last visit</summary>
        public DateTime? lastvisit;

        /// <summary>Time of last visit, expressed as a Unix timestamp in seconds</summary>
        public long? lastVisitSeconds => (visits > 1 && lastvisit != null) ? (long?)((DateTime)lastvisit).Subtract(new DateTime(1970, 1, 1)).TotalSeconds : null;

        /// <summary>comment on this starsystem</summary>
        public string comment;

        /// <summary>distance from home</summary>
        public decimal? distancefromhome;

        // Admin - the last time the information present changed
        public long? updatedat;

        // Admin - the last time the data about this system was obtained from remote repository
        public DateTime lastupdated;

        [JsonExtensionData]
        private IDictionary<string, JToken> additionalJsonData;

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            if (systemState == null)
            {
                string name = (string)additionalJsonData?["state"];
                if (name != null)
                {
                    systemState = State.FromEDName(name) ?? State.FromName(name);
                }
            }
            else
            {
                // get the canonical SystemState object for the given EDName
                systemState = State.FromEDName(systemState.edname) ?? State.None;
            }
            additionalJsonData = null;
        }

        public StarSystem()
        {
            stations = new List<Station>();
            bodies = new List<Body>();
        }
    }
}