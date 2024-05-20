﻿using EddiCargoMonitor;
using EddiConfigService;
using EddiConfigService.Configurations;
using EddiDataDefinitions;
using EddiJournalMonitor;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace UnitTests
{
    [TestClass]
    public class CargoMonitorTests : TestBase
    {
        readonly CargoMonitor cargoMonitor = new CargoMonitor(new CargoMonitorConfiguration());

        private const string cargoConfigJson = @"{
                    ""cargo"": [{
                        ""edname"": ""DamagedEscapePod"",
                        ""stolen"": 0,
                        ""haulage"": 0,
                        ""owned"": 4,
                        ""price"": 11912.0,
                        ""haulageData"": [{
                                ""missionid"": 413563829,
                                ""name"": ""Mission_Salvage_Expansion"",
                                ""typeEDName"": ""Salvage"",
                                ""status"": ""Active"",
                                ""originsystem"": ""HIP 20277"",
                                ""sourcesystem"": ""Bunuson"",
                                ""sourcebody"": null,
                                ""amount"": 4,
                                ""remaining"": 4,
                                ""startmarketid"": 0,
                                ""endmarketid"": 0,
                                ""collected"": 0,
                                ""delivered"": 0,
                                ""expiry"": null,
                                ""shared"": false
                                }]
                        }, 
                        {
                        ""edname"": ""USSCargoBlackBox"",
                        ""stolen"": 4,
                        ""haulage"": 0,
                        ""owned"": 0,
                        ""price"": 6995.0,
                        ""haulageData"": []
                        }, 
                        {
                        ""edname"": ""Drones"",
                        ""stolen"": 0,
                        ""haulage"": 0,
                        ""owned"": 21,
                        ""price"": 101.0,
                        ""haulageData"": []
                        }],
                    ""cargocarried"": 29,
                    ""updatedat"": ""2022-10-02T10:31:52Z""
            }";

        [TestInitialize]
        public void StartTestCargoMonitor()
        {
            MakeSafe();
        }

        [TestMethod]
        public void TestCargoConfig()
        {
            var config = ConfigService.FromJson<CargoMonitorConfiguration>(cargoConfigJson);

            Assert.AreEqual(3, config.cargo.Count);
            var cargo = config.cargo.FirstOrDefault(c => c.edname.Equals("DamagedEscapePod", StringComparison.InvariantCultureIgnoreCase));
            Assert.IsNotNull(cargo);
            Assert.AreEqual("Damaged Escape Pod", cargo.commodityDef.invariantName);
            Assert.AreEqual(4, cargo.total);
            Assert.AreEqual(4, cargo.owned);
            Assert.AreEqual(4, cargo.need);
            Assert.AreEqual(0, cargo.stolen);
            Assert.AreEqual(0, cargo.haulage);
            Assert.AreEqual(11912, cargo.price);

            // Verify haulage object 
            Assert.AreEqual(1, cargo.haulageData.Count());
            Haulage haulage = cargo.haulageData[0];
            Assert.AreEqual(413563829, haulage.missionid);
            Assert.AreEqual("Mission_Salvage_Expansion", haulage.name);
            Assert.AreEqual("Salvage", haulage.typeEDName);
            Assert.AreEqual(4, haulage.amount);
            Assert.AreEqual(4, haulage.remaining);
            Assert.IsFalse(haulage.shared);
        }

        [TestMethod]
        public void TestCargoEventsScenario()
        {
            var privateObject = new PrivateObject(cargoMonitor);

            // 'Startup' CargoEvent
            var line = "{ \"timestamp\":\"2018-10-31T01:54:40Z\", \"event\":\"Missions\", \"Active\":[  ], \"Failed\":[  ], \"Complete\":[  ] }";
            var events = JournalMonitor.ParseJournalEntry(line);
            privateObject.Invoke("_handleMissionsEvent", events[0] );
            line = "{ \"timestamp\":\"2018-10-31T03:39:10Z\", \"event\":\"Cargo\", \"Count\":52, \"Inventory\":[ { \"Name\":\"hydrogenfuel\", \"Name_Localised\":\"Hydrogen Fuel\", \"Count\":1, \"Stolen\":0 }, { \"Name\":\"biowaste\", \"MissionID\":426282789, \"Count\":30, \"Stolen\":0 }, { \"Name\":\"animalmeat\", \"Name_Localised\":\"Animal Meat\", \"Count\":1, \"Stolen\":0 }, { \"Name\":\"drones\", \"Name_Localised\":\"Limpet\", \"Count\":20, \"Stolen\":0 } ] }";
            events = JournalMonitor.ParseJournalEntry(line);
            privateObject.Invoke("_handleCargoEvent", events[0] );
            Assert.AreEqual(4, cargoMonitor.inventory.Count);
            Assert.AreEqual(52, cargoMonitor.cargoCarried);

            var cargo = cargoMonitor.inventory.FirstOrDefault(c => c.edname.Equals("Drones", StringComparison.InvariantCultureIgnoreCase));
            Assert.IsNotNull(cargo);
            Assert.AreEqual("Limpet", cargo.localizedName);
            Assert.AreEqual(20, cargo.total);
            Assert.AreEqual(20, cargo.owned);
            Assert.AreEqual(0, cargo.need + cargo.stolen + cargo.haulage);

            // Drone count reduced with subsequent startup CargoEvent
            line = "{ \"timestamp\":\"2018-10-31T03:39:10Z\", \"event\":\"Cargo\", \"Count\":42, \"Inventory\":[ { \"Name\":\"hydrogenfuel\", \"Name_Localised\":\"Hydrogen Fuel\", \"Count\":1, \"Stolen\":0 }, { \"Name\":\"biowaste\", \"MissionID\":426282789, \"Count\":30, \"Stolen\":0 }, { \"Name\":\"animalmeat\", \"Name_Localised\":\"Animal Meat\", \"Count\":1, \"Stolen\":0 }, { \"Name\":\"drones\", \"Name_Localised\":\"Limpet\", \"Count\":10, \"Stolen\":0 } ] }";
            events = JournalMonitor.ParseJournalEntry(line);
            privateObject.Invoke("_handleCargoEvent", events[0] );
            Assert.AreEqual(4, cargoMonitor.inventory.Count);
            Assert.AreEqual(42, cargoMonitor.cargoCarried);
            cargo = cargoMonitor.inventory.FirstOrDefault(c => c.edname.Equals("Drones", StringComparison.InvariantCultureIgnoreCase));
            Assert.IsNotNull(cargo);
            Assert.AreEqual(10, cargo.total);

            // Drones removed from inventory with subsequent startup CargoEvent
            line = "{ \"timestamp\":\"2018-10-31T03:39:10Z\", \"event\":\"Cargo\", \"Count\":32, \"Inventory\":[ { \"Name\":\"hydrogenfuel\", \"Name_Localised\":\"Hydrogen Fuel\", \"Count\":1, \"Stolen\":0 }, { \"Name\":\"biowaste\", \"MissionID\":426282789, \"Count\":30, \"Stolen\":0 }, { \"Name\":\"animalmeat\", \"Name_Localised\":\"Animal Meat\", \"Count\":1, \"Stolen\":0 } ] }";
            events = JournalMonitor.ParseJournalEntry(line);
            privateObject.Invoke("_handleCargoEvent", events[0] );
            Assert.AreEqual(3, cargoMonitor.inventory.Count);
            Assert.AreEqual(32, cargoMonitor.cargoCarried);
            cargo = cargoMonitor.inventory.FirstOrDefault( c => c.edname.Equals( "Drones", StringComparison.InvariantCultureIgnoreCase ) );
            Assert.IsNull(cargo);
        }

        [ TestMethod ]
        public void TestCommodityPurchasedEvent ()
        {
            var privateObject = new PrivateObject(cargoMonitor);

            var line = "{ \"timestamp\":\"2018-10-31T03:39:10Z\", \"event\":\"Cargo\", \"Count\":52, \"Inventory\":[ { \"Name\":\"hydrogenfuel\", \"Name_Localised\":\"Hydrogen Fuel\", \"Count\":1, \"Stolen\":0 }, { \"Name\":\"biowaste\", \"MissionID\":426282789, \"Count\":30, \"Stolen\":0 }, { \"Name\":\"animalmeat\", \"Name_Localised\":\"Animal Meat\", \"Count\":1, \"Stolen\":0 }, { \"Name\":\"drones\", \"Name_Localised\":\"Limpet\", \"Count\":20, \"Stolen\":0 } ] }";
            var events = JournalMonitor.ParseJournalEntry( line );
            privateObject.Invoke( "_handleCargoEvent", events[ 0 ] );
            Assert.AreEqual( 4, cargoMonitor.inventory.Count );
            Assert.AreEqual( 52, cargoMonitor.cargoCarried );

            var cargo = cargoMonitor.inventory.FirstOrDefault( c => c.edname.Equals("AnimalMeat", StringComparison.InvariantCultureIgnoreCase) );
            Assert.IsNotNull( cargo );
            Assert.AreEqual( "Animal Meat", cargo.localizedName );
            Assert.AreEqual( 1, cargo.total );
            Assert.AreEqual( 1, cargo.owned );
            Assert.AreEqual( 0, cargo.need + cargo.stolen + cargo.haulage );

            line = @"{ ""timestamp"":""2021-01-17T05:56:13Z"", ""event"":""MarketBuy"", ""MarketID"":3222000896, ""Type"":""animalmeat"", ""Type_Localised"":""Animal Meat"", ""Count"":105, ""BuyPrice"":1503, ""TotalCost"":157815 }";
            events = JournalMonitor.ParseJournalEntry( line );
            privateObject.Invoke( "_handleCommodityPurchasedEvent", events[ 0 ] );
            cargo = cargoMonitor.inventory.FirstOrDefault( c => c.edname.Equals("AnimalMeat", StringComparison.InvariantCultureIgnoreCase) );
            Assert.IsNotNull( cargo );
            Assert.IsFalse( cargo.haulageData.Any() );
            Assert.AreEqual( 106, cargo.total );
            Assert.AreEqual( 106, cargo.owned );
            Assert.AreEqual( 0, cargo.need + cargo.stolen + cargo.haulage );
        }

        [ TestMethod ]
        public void TestCommodityEjectedEvent ()
        {
            var privateObject = new PrivateObject(cargoMonitor);

            var line = "{ \"timestamp\":\"2018-10-31T03:39:10Z\", \"event\":\"Cargo\", \"Count\":52, \"Inventory\":[ { \"Name\":\"hydrogenfuel\", \"Name_Localised\":\"Hydrogen Fuel\", \"Count\":1, \"Stolen\":0 }, { \"Name\":\"biowaste\", \"MissionID\":426282789, \"Count\":30, \"Stolen\":0 }, { \"Name\":\"animalmeat\", \"Name_Localised\":\"Animal Meat\", \"Count\":1, \"Stolen\":0 }, { \"Name\":\"drones\", \"Name_Localised\":\"Limpet\", \"Count\":20, \"Stolen\":0 } ] }";
            var events = JournalMonitor.ParseJournalEntry( line );
            privateObject.Invoke( "_handleCargoEvent", events[ 0 ] );
            Assert.AreEqual( 4, cargoMonitor.inventory.Count );
            Assert.AreEqual( 52, cargoMonitor.cargoCarried );

            var cargo = cargoMonitor.inventory.FirstOrDefault( c => c.edname.Equals("Biowaste", StringComparison.InvariantCultureIgnoreCase) );
            Assert.IsNotNull( cargo );
            Assert.AreEqual( 30, cargo.total );
            Assert.AreEqual( 30, cargo.haulage );
            var haulage = cargo.haulageData.FirstOrDefault();
            Assert.IsNotNull( haulage );
            Assert.AreEqual( 426282789, haulage.missionid );
            Assert.AreEqual( "Mission_None", haulage.name );
            Assert.AreEqual( 30, haulage.amount );
            Assert.AreEqual( "Active", haulage.status );

            haulage.typeEDName = "delivery";
            line = @"{""timestamp"": ""2016-06-10T14:32:03Z"", ""event"": ""EjectCargo"", ""Type"":""biowaste"", ""Count"":2, ""MissionID"":426282789, ""Abandoned"":true}";
            events = JournalMonitor.ParseJournalEntry( line );
            privateObject.Invoke( "_handleCommodityEjectedEvent", events[ 0 ] );

            cargo = cargoMonitor.inventory.FirstOrDefault( c => c.edname.Equals("Biowaste", StringComparison.InvariantCultureIgnoreCase) );
            Assert.IsNotNull( cargo );
            haulage = cargo.haulageData.FirstOrDefault( h => h.missionid == 426282789 );
            Assert.IsNotNull( haulage );
            Assert.AreEqual( "Failed", haulage.status );
            Assert.AreEqual( 28, cargo.total );
            Assert.AreEqual( 28, cargo.haulage );
            Assert.AreEqual( 30, cargo.need );
        }

        [ TestMethod ]
        public void TestCommoditySoldEvent ()
        {
            var privateObject = new PrivateObject(cargoMonitor);

            var line = "{ \"timestamp\":\"2018-10-31T03:39:10Z\", \"event\":\"Cargo\", \"Count\":52, \"Inventory\":[ { \"Name\":\"hydrogenfuel\", \"Name_Localised\":\"Hydrogen Fuel\", \"Count\":1, \"Stolen\":0 }, { \"Name\":\"biowaste\", \"MissionID\":426282789, \"Count\":30, \"Stolen\":0 }, { \"Name\":\"animalmeat\", \"Name_Localised\":\"Animal Meat\", \"Count\":1, \"Stolen\":0 }, { \"Name\":\"drones\", \"Name_Localised\":\"Limpet\", \"Count\":20, \"Stolen\":0 } ] }";
            var events = JournalMonitor.ParseJournalEntry( line );
            privateObject.Invoke( "_handleCargoEvent", events[ 0 ] );
            Assert.AreEqual( 4, cargoMonitor.inventory.Count );
            Assert.AreEqual( 52, cargoMonitor.cargoCarried );

            var cargo = cargoMonitor.inventory.FirstOrDefault( c => c.edname.Equals( "HydrogenFuel", StringComparison.InvariantCultureIgnoreCase ) );
            Assert.IsNotNull( cargo );
            Assert.AreEqual( "Hydrogen Fuel", cargo.localizedName );
            Assert.AreEqual( 1, cargo.total );
            Assert.AreEqual( 1, cargo.owned );
            Assert.AreEqual( 0, cargo.need + cargo.stolen + cargo.haulage );

            line = @"{ ""timestamp"":""2022-02-06T04:35:37Z"", ""event"":""MarketSell"", ""MarketID"":3502759680, ""Type"":""hydrogenfuel"", ""Type_Localised"":""Hydrogen Fuel"", ""Count"":1, ""SellPrice"":100, ""TotalSale"":100, ""AvgPricePaid"":84 }";
            events = JournalMonitor.ParseJournalEntry( line );
            privateObject.Invoke( "_handleCommoditySoldEvent", events[ 0 ] );
            cargo = cargoMonitor.inventory.FirstOrDefault( c => c.edname.Equals( "HydrogenFuel", StringComparison.InvariantCultureIgnoreCase ) );
            Assert.IsNull( cargo );
        }

        [TestMethod]
        public void TestCargoMissionScenario()
        {
            var privateObject = new PrivateObject(cargoMonitor);

            // CargoEvent
            var line = "{ \"timestamp\":\"2018-10-31T03:39:10Z\", \"event\":\"Cargo\", \"Count\":32, \"Inventory\":[ { \"Name\":\"hydrogenfuel\", \"Name_Localised\":\"Hydrogen Fuel\", \"Count\":1, \"Stolen\":0 }, { \"Name\":\"biowaste\", \"MissionID\":426282789, \"Count\":30, \"Stolen\":0 }, { \"Name\":\"animalmeat\", \"Name_Localised\":\"Animal Meat\", \"Count\":1, \"Stolen\":0 } ] }";
            var events = JournalMonitor.ParseJournalEntry(line);
            privateObject.Invoke("_handleCargoEvent", new object[] { events[0] });

            // CargoMissionAcceptedEvent - Check to see if this is a cargo mission and update our inventory accordingly
            line = @"{ ""timestamp"": ""2018-05-05T19:42:20Z"", ""event"": ""MissionAccepted"", ""Faction"": ""Elite Knights"", ""Name"": ""Mission_Salvage_Planet"", ""LocalisedName"": ""Salvage 3 Structural Regulators"", ""Commodity"": ""$StructuralRegulators_Name;"", ""Commodity_Localised"": ""Structural Regulators"", ""Count"": 3, ""DestinationSystem"": ""Merope"", ""Expiry"": ""2018-05-12T15:20:27Z"", ""Wing"": false, ""Influence"": ""Med"", ""Reputation"": ""Med"", ""Reward"": 557296, ""MissionID"": 375682327 }";
            events = JournalMonitor.ParseJournalEntry(line);
            privateObject.Invoke("_handleMissionAcceptedEvent", new object[] { events[0] });
            line = @"{ ""timestamp"": ""2018-05-05T19:42:20Z"", ""event"": ""MissionAccepted"", ""Faction"": ""Merope Expeditionary Fleet"", ""Name"": ""Mission_Salvage_Planet"", ""LocalisedName"": ""Salvage 4 Structural Regulators"", ""Commodity"": ""$StructuralRegulators_Name;"", ""Commodity_Localised"": ""Structural Regulators"", ""Count"": 4, ""DestinationSystem"": ""HIP 17692"", ""Expiry"": ""2018-05-12T15:20:27Z"", ""Wing"": false, ""Influence"": ""Med"", ""Reputation"": ""Med"", ""Reward"": 557296, ""MissionID"": 375660729 }";
            events = JournalMonitor.ParseJournalEntry(line);
            privateObject.Invoke("_handleMissionAcceptedEvent", new object[] { events[0] });

            // Verify cargo populated properly
            var cargo = cargoMonitor.inventory.FirstOrDefault(c => c.edname.Equals( "StructuralRegulators", StringComparison.InvariantCultureIgnoreCase ));
            Assert.IsNotNull(cargo);
            Assert.AreEqual("Structural Regulators", cargo.invariantName);
            Assert.AreEqual(0, cargo.total);
            Assert.AreEqual(0, cargo.haulage + cargo.stolen + cargo.owned);
            Assert.AreEqual(7, cargo.need);
            Assert.AreEqual(2, cargo.haulageData.Count);

            // Verify haulage populated properly
            var haulage = cargo.haulageData.FirstOrDefault(h => h.missionid == 375682327);
            Assert.IsNotNull(haulage);
            Assert.AreEqual(3, haulage.amount);
            Assert.AreEqual("Mission_Salvage_Planet", haulage.name);

            // Verify duplication protection
            events = JournalMonitor.ParseJournalEntry(line);
            privateObject.Invoke("_handleMissionAcceptedEvent", new object[] { events[0] });
            cargo = cargoMonitor.inventory.FirstOrDefault(c => c.edname.Equals( "StructuralRegulators", StringComparison.InvariantCultureIgnoreCase ));
            Assert.IsNotNull(cargo);
            Assert.AreEqual(7, cargo.need);
            Assert.AreEqual(2, cargo.haulageData.Count);

            // CargoEvent - Collected 2 Structural Regulators for mission ID 375682327. Verify haulage changed but not need
            line = "{ \"timestamp\":\"2018-10-31T03:39:10Z\", \"event\":\"Cargo\", \"Count\":34, \"Inventory\":[ { \"Name\":\"hydrogenfuel\", \"Name_Localised\":\"Hydrogen Fuel\", \"Count\":1, \"Stolen\":0 }, { \"Name\":\"biowaste\", \"MissionID\":426282789, \"Count\":30, \"Stolen\":0 }, { \"Name\":\"animalmeat\", \"Name_Localised\":\"Animal Meat\", \"Count\":1, \"Stolen\":0 }, { \"Name\":\"structuralregulators\", \"MissionID\":375682327, \"Count\":2, \"Stolen\":0 } ] }";
            events = JournalMonitor.ParseJournalEntry(line);
            privateObject.Invoke("_handleCargoEvent", new object[] { events[0] });

            cargo = cargoMonitor.inventory.FirstOrDefault( c => c.edname.Equals( "StructuralRegulators", StringComparison.InvariantCultureIgnoreCase ) );
            Assert.IsNotNull(cargo);
            Assert.AreEqual(2, cargo.total);
            Assert.AreEqual(2, cargo.haulage);
            Assert.AreEqual(7, cargo.need);
            Assert.AreEqual(0, cargo.stolen + cargo.owned);

            // CargoMissionFailedEvent
            line = @"{ ""timestamp"":""2018-05-05T19:42:20Z"", ""event"":""MissionFailed"", ""Name"":""Mission_Salvage_Planet"", ""MissionID"":375682327 }";
            events = JournalMonitor.ParseJournalEntry( line );
            privateObject.Invoke( "_handleMissionFailedEvent", new object[] { events[ 0 ] } );
            haulage = cargo.haulageData.FirstOrDefault( h => h.missionid == 375682327 );
            Assert.IsNotNull( haulage );

            // Cargo MissionAbandonedEvent - Verify haulage data for for mission ID 375682327 has been removed
            line = @"{ ""timestamp"":""2018-05-05T19:42:20Z"", ""event"":""MissionAbandoned"", ""Name"":""Mission_Salvage_Planet"", ""MissionID"":375682327 }";
            events = JournalMonitor.ParseJournalEntry(line);
            privateObject.Invoke("_handleMissionAbandonedEvent", new object[] { events[0] });
            haulage = cargo.haulageData.FirstOrDefault(h => h.missionid == 375682327);
            Assert.IsNull(haulage);

            // CargoEvent - Verify 2 stolen Structural Regulators and 4 still needed for mission ID 37566072
            line = "{ \"timestamp\":\"2018-10-31T03:39:10Z\", \"event\":\"Cargo\", \"Count\":34, \"Inventory\":[ { \"Name\":\"hydrogenfuel\", \"Name_Localised\":\"Hydrogen Fuel\", \"Count\":1, \"Stolen\":0 }, { \"Name\":\"biowaste\", \"MissionID\":426282789, \"Count\":30, \"Stolen\":0 }, { \"Name\":\"animalmeat\", \"Name_Localised\":\"Animal Meat\", \"Count\":1, \"Stolen\":0 }, { \"Name\":\"structuralregulators\", \"Count\":2, \"Stolen\":2 } ] }";
            events = JournalMonitor.ParseJournalEntry(line);
            privateObject.Invoke("_handleCargoEvent", new object[] { events[0] });
            cargo = cargoMonitor.inventory.FirstOrDefault( c => c.edname.Equals( "StructuralRegulators", StringComparison.InvariantCultureIgnoreCase ) );
            Assert.IsNotNull(cargo);
            Assert.AreEqual(2, cargo.total);
            Assert.AreEqual(2, cargo.stolen);
            Assert.AreEqual(4, cargo.need);
            Assert.AreEqual(0, cargo.haulage + cargo.owned);

            // CargoMissionCompletedEvent - Verify haulage data & cargo has been removed
            line = @"{ ""timestamp"": ""2018-05-05T22:27:58Z"", ""event"": ""MissionCompleted"", ""Faction"": ""Merope Expeditionary Fleet"", ""Name"": ""Mission_Salvage_Planet_name"", ""MissionID"": 375660729, ""Commodity"": ""$StructuralRegulators_Name;"", ""Commodity_Localised"": ""Structural Regulators"", ""Count"": 4, ""DestinationSystem"": ""HIP 17692"", ""Reward"": 624016, ""FactionEffects"": [ { ""Faction"": ""Merope Expeditionary Fleet"", ""Effects"": [ { ""Effect"": ""$MISSIONUTIL_Interaction_Summary_civilUnrest_down;"", ""Effect_Localised"": ""$#MinorFaction; are happy to report improved civil contentment, making a period of civil unrest unlikely."", ""Trend"": ""DownGood"" } ], ""Influence"": [ { ""SystemAddress"": 224644818084, ""Trend"": ""UpGood"" } ], ""Reputation"": ""UpGood"" } ] }";
            events = JournalMonitor.ParseJournalEntry(line);
            privateObject.Invoke("_handleMissionCompletedEvent", new object[] { events[0] });
            cargo = cargoMonitor.inventory.FirstOrDefault( c => c.edname.Equals( "StructuralRegulators", StringComparison.InvariantCultureIgnoreCase ) );
            Assert.IsNull( cargo );

            line = "{ \"timestamp\":\"2018-10-31T03:39:10Z\", \"event\":\"Cargo\", \"Count\":32, \"Inventory\":[ { \"Name\":\"hydrogenfuel\", \"Name_Localised\":\"Hydrogen Fuel\", \"Count\":1, \"Stolen\":0 }, { \"Name\":\"biowaste\", \"MissionID\":426282789, \"Count\":30, \"Stolen\":0 }, { \"Name\":\"animalmeat\", \"Name_Localised\":\"Animal Meat\", \"Count\":1, \"Stolen\":0 } ] }";
            events = JournalMonitor.ParseJournalEntry(line);
            privateObject.Invoke("_handleCargoEvent", new object[] { events[0] });
            cargo = cargoMonitor.inventory.FirstOrDefault( c => c.edname.Equals( "StructuralRegulators", StringComparison.InvariantCultureIgnoreCase ) );
            Assert.IsNull(cargo);

            line = @"{ ""timestamp"": ""2018-05-05T19:42:20Z"", ""event"": ""MissionAccepted"", ""Faction"": ""Elite Knights"", ""Name"": ""Mission_Salvage_Planet"", ""LocalisedName"": ""Salvage 3 Structural Regulators"", ""Commodity"": ""$StructuralRegulators_Name;"", ""Commodity_Localised"": ""Structural Regulators"", ""Count"": 3, ""DestinationSystem"": ""Merope"", ""Expiry"": ""2018-05-12T15:20:27Z"", ""Wing"": false, ""Influence"": ""Med"", ""Reputation"": ""Med"", ""Reward"": 557296, ""MissionID"": 375682327 }";
            events = JournalMonitor.ParseJournalEntry(line);
            privateObject.Invoke("_handleMissionAcceptedEvent", new object[] { events[0] });
            cargo = cargoMonitor.inventory.FirstOrDefault( c => c.edname.Equals( "StructuralRegulators", StringComparison.InvariantCultureIgnoreCase ) );
            Assert.IsNotNull(cargo);

            // CargoDepotEvent - Check response for missed 'Mission accepted' event. Verify both cargo and haulage are created
            line = @"{ ""timestamp"":""2018-08-26T02:55:10Z"", ""event"":""CargoDepot"", ""MissionID"":413748324, ""UpdateType"":""Deliver"", ""CargoType"":""Tantalum"", ""Count"":54, ""StartMarketID"":0, ""EndMarketID"":3224777216, ""ItemsCollected"":0, ""ItemsDelivered"":54, ""TotalItemsToDeliver"":70, ""Progress"":0.000000 }";
            events = JournalMonitor.ParseJournalEntry(line);
            privateObject.Invoke("_handleCargoDepotEvent", new object[] { events[0] });
            cargo = cargoMonitor.inventory.FirstOrDefault(c => c.edname.Equals( "Tantalum", StringComparison.InvariantCultureIgnoreCase ) );
            Assert.IsNotNull(cargo);
            Assert.AreEqual(0, cargo.total);
            Assert.AreEqual(0, cargo.haulage + cargo.owned);
            Assert.AreEqual(16, cargo.need);
            haulage = cargo.haulageData.FirstOrDefault(h => h.missionid == 413748324);
            Assert.IsNotNull(haulage);
            Assert.AreEqual(16, haulage.remaining);
            Assert.IsTrue(haulage.shared);

            // Cargo Delivery 'Mission accepted' Event with 'Cargo Depot' events
            line = @"{ ""timestamp"":""2018-08-26T00:50:48Z"", ""event"":""MissionAccepted"", ""Faction"":""Calennero State Industries"", ""Name"":""Mission_Delivery_Boom"", ""LocalisedName"":""Boom time delivery of 60 units of Silver"", ""Commodity"":""$Silver_Name;"", ""Commodity_Localised"":""Silver"", ""Count"":60, ""DestinationSystem"":""HIP 20277"", ""DestinationStation"":""Fabian City"", ""Expiry"":""2018-08-27T00:48:38Z"", ""Wing"":false, ""Influence"":""Med"", ""Reputation"":""Med"", ""Reward"":25000000, ""MissionID"":413748339 }";
            events = JournalMonitor.ParseJournalEntry(line);
            privateObject.Invoke("_handleMissionAcceptedEvent", new object[] { events[0] });
            cargo = cargoMonitor.inventory.FirstOrDefault(c => c.edname.Equals( "Silver", StringComparison.InvariantCultureIgnoreCase ) );
            Assert.IsNotNull(cargo);
            Assert.AreEqual(0, cargo.total);
            Assert.AreEqual(0, cargo.haulage + cargo.owned);
            Assert.AreEqual(60, cargo.need);
            haulage = cargo.haulageData.FirstOrDefault(h => h.missionid == 413748339);
            Assert.IsNotNull(haulage);
            Assert.AreEqual(60, haulage.remaining);
            Assert.IsFalse(haulage.shared);

            line = @"{ ""timestamp"":""2018-08-26T02:55:10Z"", ""event"":""CargoDepot"", ""MissionID"":413748339, ""UpdateType"":""Collect"", ""CargoType"":""Silver"", ""Count"":60, ""StartMarketID"":3225297216, ""EndMarketID"":3224777216, ""ItemsCollected"":60, ""ItemsDelivered"":0, ""TotalItemsToDeliver"":60, ""Progress"":0.000000 }";
            events = JournalMonitor.ParseJournalEntry(line);
            privateObject.Invoke("_handleCargoDepotEvent", new object[] { events[0] });

            line = "{ \"timestamp\":\"2018-10-31T03:39:10Z\", \"event\":\"Cargo\", \"Count\":92, \"Inventory\":[ { \"Name\":\"hydrogenfuel\", \"Name_Localised\":\"Hydrogen Fuel\", \"Count\":1, \"Stolen\":0 }, { \"Name\":\"biowaste\", \"MissionID\":426282789, \"Count\":30, \"Stolen\":0 }, { \"Name\":\"animalmeat\", \"Name_Localised\":\"Animal Meat\", \"Count\":1, \"Stolen\":0 }, { \"Name\":\"silver\", \"MissionID\":413748339, \"Count\":60, \"Stolen\":0 } ] }";
            events = JournalMonitor.ParseJournalEntry(line);
            privateObject.Invoke("_handleCargoEvent", new object[] { events[0] });
            Assert.AreEqual(60, cargo.total);
            Assert.AreEqual(60, cargo.haulage);
            Assert.AreEqual(60, cargo.need);
            Assert.AreEqual(60, haulage.remaining);
            Assert.AreEqual(3225297216, haulage.startmarketid);
            Assert.AreEqual(3224777216, haulage.endmarketid);

            line = @"{ ""timestamp"":""2018-08-26T03:55:10Z"", ""event"":""CargoDepot"", ""MissionID"":413748339, ""UpdateType"":""Deliver"", ""CargoType"":""Silver"", ""Count"":60, ""StartMarketID"":3225297216, ""EndMarketID"":3224777216, ""ItemsCollected"":60, ""ItemsDelivered"":60, ""TotalItemsToDeliver"":60, ""Progress"":0.000000 }";
            events = JournalMonitor.ParseJournalEntry(line);
            privateObject.Invoke("_handleCargoDepotEvent", new object[] { events[0] });
            Assert.AreEqual(0, haulage.remaining);
            Assert.AreEqual(0, haulage.need);
            Assert.AreEqual(0, cargo.need);
        }

        [TestMethod]
        public void TestCargoPriceScenario()
        {
            // Test that average cargo price dynamically updates based on the aquisition prices and quantities
            var privateObject = new PrivateObject(cargoMonitor);

            // Synthesise 4 drones
            var line = @"{ ""timestamp"":""2020-10-26T04:05:27Z"", ""event"":""Synthesis"", ""Name"":""Limpet Basic"", ""Materials"":[ { ""Name"":""iron"", ""Count"":10 }, { ""Name"":""nickel"", ""Count"":10 } ] }";
            var events = JournalMonitor.ParseJournalEntry(line);
            privateObject.Invoke("_handleSynthesisedEvent", new object[] { events[0] });
            var cargo = cargoMonitor.inventory.ToList().FirstOrDefault(c => c.edname == "Drones");
            Assert.IsNotNull(cargo);
            Assert.AreEqual(4, cargo.total);
            Assert.AreEqual(0, cargo.haulage);
            Assert.AreEqual(0, cargo.need);
            Assert.AreEqual(0, cargo.stolen);
            Assert.AreEqual(4, cargo.owned);
            Assert.AreEqual(0, cargo.price); // weighted price: 0

            // Buy one drone
            line = @"{ ""timestamp"":""2020-10-26T04:10:27Z"", ""event"":""BuyDrones"", ""Type"":""Drones"", ""Count"":1, ""BuyPrice"":127, ""TotalCost"":127 }";
            events = JournalMonitor.ParseJournalEntry(line);
            privateObject.Invoke("_handleLimpetPurchasedEvent", new object[] { events[0] });
            Assert.AreEqual(5, cargo.total);
            Assert.AreEqual(0, cargo.haulage);
            Assert.AreEqual(0, cargo.need);
            Assert.AreEqual(0, cargo.stolen);
            Assert.AreEqual(5, cargo.owned);
            Assert.AreEqual(25, cargo.price); // weighted price: 25.4

            // Buy 5 drones
            line = @"{ ""timestamp"":""2020-10-26T04:15:27Z"", ""event"":""BuyDrones"", ""Type"":""Drones"", ""Count"":5, ""BuyPrice"":127, ""TotalCost"":635 }";
            events = JournalMonitor.ParseJournalEntry(line);
            privateObject.Invoke("_handleLimpetPurchasedEvent", new object[] { events[0] });
            Assert.AreEqual(10, cargo.total);
            Assert.AreEqual(0, cargo.haulage);
            Assert.AreEqual(0, cargo.need);
            Assert.AreEqual(0, cargo.stolen);
            Assert.AreEqual(10, cargo.owned);
            Assert.AreEqual(76, cargo.price); // weighted price: 76.2

            // Buy another 5 drones, except these are on sale
            line = @"{ ""timestamp"":""2020-10-26T04:15:27Z"", ""event"":""BuyDrones"", ""Type"":""Drones"", ""Count"":5, ""BuyPrice"":1, ""TotalCost"":5 }";
            events = JournalMonitor.ParseJournalEntry(line);
            privateObject.Invoke("_handleLimpetPurchasedEvent", new object[] { events[0] });
            Assert.AreEqual(15, cargo.total);
            Assert.AreEqual(0, cargo.haulage);
            Assert.AreEqual(0, cargo.need);
            Assert.AreEqual(0, cargo.stolen);
            Assert.AreEqual(15, cargo.owned);
            Assert.AreEqual(51, cargo.price); // weighted price: 51.13
        }
    }
}
