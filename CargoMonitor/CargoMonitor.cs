﻿using EddiConfigService;
using EddiConfigService.Configurations;
using EddiCore;
using EddiDataDefinitions;
using EddiEvents;
using JetBrains.Annotations;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Utilities;

namespace EddiCargoMonitor
{
    /// Monitor cargo for the current ship
    public class CargoMonitor : IEddiMonitor
    {
        // Observable collection for us to handle changes
        public ObservableCollection<Cargo> inventory { get; private set; } = new ObservableCollection<Cargo>();
        public int cargoCarried;
        private bool checkHaulage = false;
        private DateTime updateDat;

        private static readonly object inventoryLock = new object();
        public event EventHandler InventoryUpdatedEvent;

        private static readonly Dictionary<string, string> CHAINED = new Dictionary<string, string>()
        {
            {"clearingthepath", "delivery"},
            {"helpfinishtheorder", "delivery"},
            {"rescuefromthetwins", "salvage"},
            {"rescuethewares", "salvage"}
        };

        public string MonitorName()
        {
            return "Cargo monitor";
        }

        public string LocalizedMonitorName()
        {
            return Properties.CargoMonitor.cargo_monitor_name;
        }

        public string MonitorDescription()
        {
            return Properties.CargoMonitor.cargo_monitor_desc;
        }

        public bool IsRequired()
        {
            return true;
        }

        /// <summary>
        /// Create a new CargoMonitor, reading the configuration from the default location on the file system.
        /// This is required for the DLL to load
        /// </summary>
        [UsedImplicitly]
        public CargoMonitor() : this(null)
        { }

        /// <summary>
        /// Create a new CargoMonitor, optionally passing in a non-default configuration
        /// </summary>
        /// <param name="configuration">The configuration to use. If null, it will be read from the file system</param>
        public CargoMonitor(CargoMonitorConfiguration configuration = null)
        {
            BindingOperations.CollectionRegistering += Inventory_CollectionRegistering;
            readInventory( configuration );
            Logging.Info( $"Initialized {MonitorName()}" );
        }

        private void Inventory_CollectionRegistering(object sender, CollectionRegisteringEventArgs e)
        {
            if (Application.Current != null)
            {
                // Synchronize this collection between threads
                BindingOperations.EnableCollectionSynchronization(inventory, inventoryLock);
            }
            else
            {
                // If started from VoiceAttack, the dispatcher is on a different thread. Invoke synchronization there.
                Dispatcher.CurrentDispatcher.Invoke(() => { BindingOperations.EnableCollectionSynchronization(inventory, inventoryLock); });
            }
        }

        public bool NeedsStart()
        {
            // We don't actively do anything, just listen to events
            return false;
        }

        public void Start()
        { }

        public void Stop()
        { }

        public void Reload()
        {
            readInventory();
            Logging.Info($"Reloaded {MonitorName()}");
        }

        public UserControl ConfigurationTabItem()
        {
            return new ConfigurationWindow();
        }

        public void HandleProfile(JObject profile)
        { }

        public void PostHandle(Event @event)
        { }

        public void PreHandle(Event @event)
        {
            // Handle the events that we care about
            if (@event is CargoEvent cargoEvent)
            {
                handleCargoEvent(cargoEvent);
            }
            else if (@event is CommodityCollectedEvent commodityCollectedEvent)
            {
                handleCommodityCollectedEvent(commodityCollectedEvent);
            }
            else if (@event is CommodityEjectedEvent commodityEjectedEvent)
            {
                handleCommodityEjectedEvent(commodityEjectedEvent);
            }
            else if (@event is CommodityPurchasedEvent commodityPurchasedEvent)
            {
                handleCommodityPurchasedEvent(commodityPurchasedEvent);
            }
            else if (@event is CommodityRefinedEvent commodityRefinedEvent)
            {
                handleCommodityRefinedEvent(commodityRefinedEvent);
            }
            else if (@event is CommoditySoldEvent commoditySoldEvent)
            {
                handleCommoditySoldEvent(commoditySoldEvent);
            }
            else if (@event is CargoDepotEvent cargoDepotEvent)
            {
                // If cargo is collected or delivered in a wing mission
                handleCargoDepotEvent(cargoDepotEvent);
            }
            else if (@event is LimpetPurchasedEvent limpetPurchasedEvent)
            {
                handleLimpetPurchasedEvent(limpetPurchasedEvent);
            }
            else if (@event is MissionsEvent missionsEvent)
            {
                // Remove cargo haulage stragglers for completed missions
                handleMissionsEvent(missionsEvent);
            }
            else if (@event is MissionAbandonedEvent missionAbandonedEvent)
            {
                // If we abandon a mission with cargo it becomes stolen
                handleMissionAbandonedEvent(missionAbandonedEvent);
            }
            else if (@event is MissionAcceptedEvent missionAcceptedEvent)
            {
                // Check to see if this is a cargo mission and update our inventory accordingly
                handleMissionAcceptedEvent(missionAcceptedEvent);
            }
            else if (@event is MissionCompletedEvent missionCompletedEvent)
            {
                // Check to see if this is a cargo mission and update our inventory accordingly
                handleMissionCompletedEvent(missionCompletedEvent);
            }
            else if (@event is MissionExpiredEvent missionExpiredEvent)
            {
                // Check to see if this is a cargo mission and update our inventory accordingly
                handleMissionExpiredEvent(missionExpiredEvent);
            }
            else if (@event is MissionFailedEvent missionFailedEvent)
            {
                // If we fail a mission with cargo it becomes stolen
                handleMissionFailedEvent(missionFailedEvent);
            }
            else if (@event is DiedEvent)
            {
                handleDiedEvent();
            }
            else if (@event is EngineerContributedEvent engineerContributedEvent)
            {
                handleEngineerContributedEvent(engineerContributedEvent);
            }
            else if (@event is SynthesisedEvent synthesisedEvent)
            {
                handleSynthesisedEvent(synthesisedEvent);
            }
        }

        private void handleCargoEvent(CargoEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                _handleCargoEvent(@event);
                writeInventory();
            }
        }

        private void _handleCargoEvent(CargoEvent @event)
        {
            if (@event.vessel == Constants.VEHICLE_SHIP)
            {
                cargoCarried = @event.cargocarried;
                if (@event.inventory != null)
                {
                    var infoList = @event.inventory.ToList();

                    // Remove strays from the manifest
                    foreach (var inventoryCargo in inventory.ToList())
                    {
                        var name = inventoryCargo.edname;
                        var infoItem = @event.inventory.FirstOrDefault(i => i.name.Equals(name, StringComparison.OrdinalIgnoreCase));
                        if (infoItem == null)
                        {
                            if (inventoryCargo.haulageData?.Any() ?? false)
                            {
                                // Keep cargo entry in manifest with zeroed amounts, if missions are pending
                                inventoryCargo.haulage = 0;
                                inventoryCargo.owned = 0;
                                inventoryCargo.stolen = 0;
                                inventoryCargo.CalculateNeed();
                            }
                            else
                            {
                                // Strip out the stray from the manifest
                                _RemoveCargoWithEDName(inventoryCargo.edname);
                            }
                        }
                    }

                    // Update existing cargo in the manifest
                    while (infoList.Any())
                    {
                        var name = infoList.ToList().First().name;
                        var cargoInfo = infoList.Where(i => i.name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
                        var cargo = inventory.FirstOrDefault(c => c.edname.Equals(name, StringComparison.OrdinalIgnoreCase));
                        if (cargo != null)
                        {
                            var total = cargoInfo.Sum(i => i.count);
                            var stolen = cargoInfo.Where(i => i.missionid == null).Sum(i => i.stolen);
                            var missionCount = cargoInfo.Count(i => i.missionid != null);
                            if (total != cargo.total || stolen != cargo.stolen || missionCount != cargo.haulageData.Count())
                            {
                                UpdateCargoFromInfo(cargo, cargoInfo);
                            }
                        }
                        else
                        {
                            // Add cargo entries for those missing
                            cargo = new Cargo(name);
                            UpdateCargoFromInfo(cargo, cargoInfo);
                        }
                        AddOrUpdateCargo(cargo);
                        infoList.RemoveAll(i => i.name == name);
                    }
                }
            }
        }

        private void handleCommodityCollectedEvent(CommodityCollectedEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                if (_handleCommodityCollectedEvent(@event))
                {
                    writeInventory();
                }
            }
        }

        private bool _handleCommodityCollectedEvent(CommodityCollectedEvent @event)
        {
            var update = false;
            var cargo = GetCargoWithEDName(@event.commodityDefinition?.edname);
            if (cargo != null)
            {
                var haulage = cargo.haulageData.FirstOrDefault(h => h.missionid == @event.missionid);
                if (EDDI.Instance?.Vehicle != Constants.VEHICLE_SHIP)
                {
                    if (haulage != null)
                    {
                        cargo.AddDetailedQty(CargoType.haulage, 1, 0);
                    }
                    else if (@event.stolen)
                    {
                        cargo.AddDetailedQty(CargoType.stolen, 1, 0);
                    }
                    else
                    {
                        cargo.AddDetailedQty(CargoType.owned, 1, 0);
                    }
                    cargo.CalculateNeed();
                    update = true;
                }
                if (haulage != null && ((haulage.typeEDName?.Contains("mining") ?? false)
                    || (haulage.typeEDName?.Contains("piracy") ?? false)
                    || (haulage.typeEDName?.Contains("rescue") ?? false)
                    || (haulage.typeEDName?.Contains("salvage") ?? false)))
                {
                    haulage.sourcesystem = EDDI.Instance?.CurrentStarSystem?.systemname;
                    haulage.sourcebody = EDDI.Instance?.CurrentStellarBody?.bodyname;
                    update = true;
                }
            }
            return update;
        }

        private void handleCommodityEjectedEvent(CommodityEjectedEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                if (_handleCommodityEjectedEvent(@event))
                {
                    writeInventory();
                }
            }
        }

        private bool _handleCommodityEjectedEvent(CommodityEjectedEvent @event)
        {
            var update = false;
            var cargo = GetCargoWithEDName(@event.commodityDefinition?.edname);
            if (cargo != null)
            {
                if (EDDI.Instance?.Vehicle != Constants.VEHICLE_SHIP)
                {
                    if (@event.missionid != null)
                    {
                        cargo.RemoveDetailedQty(CargoType.haulage, @event.amount, (long)@event.missionid);
                    }
                    else
                    {
                        cargo.RemoveDetailedQty(CargoType.owned, @event.amount);
                    }
                    cargo.CalculateNeed();
                    update = true;
                }

                var haulage = cargo.haulageData.FirstOrDefault(h => h.missionid == @event.missionid);
                if (haulage != null)
                {
                    switch (haulage.typeEDName)
                    {
                        case "delivery":
                        case "deliverywing":
                        case "smuggle":
                            {
                                haulage.status = "Failed";
                                var mission = ConfigService.Instance.missionMonitorConfiguration
                                    ?.missions
                                    ?.FirstOrDefault(m => m.missionid == @event.missionid);
                                if (mission != null)
                                {
                                    mission.statusDef = MissionStatus.Failed;
                                }
                                update = true;
                            }
                            break;
                    }
                }
            }
            return update;
        }

        private void handleCommodityPurchasedEvent(CommodityPurchasedEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                if (_handleCommodityPurchasedEvent(@event))
                {
                    writeInventory();
                }
            }
        }

        private bool _handleCommodityPurchasedEvent(CommodityPurchasedEvent @event)
        {
            var cargo = GetCargoWithEDName(@event.commodityDefinition?.edname) ?? new Cargo(@event.commodityDefinition?.edname);
            var haulage = cargo.haulageData.FirstOrDefault(h => h.typeEDName
                .ToLowerInvariant()
                .Contains("collect"));
            if (haulage != null)
            {
                haulage.sourcesystem = EDDI.Instance?.CurrentStarSystem?.systemname;
                haulage.sourcebody = EDDI.Instance?.CurrentStation?.name;
                cargo.AddDetailedQty(CargoType.haulage, @event.amount, @event.price, haulage);
            }
            else
            {
                cargo.AddDetailedQty(CargoType.owned, @event.amount, @event.price);
            }
            AddOrUpdateCargo(cargo);
            return true;
        }

        private void handleCommodityRefinedEvent(CommodityRefinedEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                if (_handleCommodityRefinedEvent(@event))
                {
                    writeInventory();
                }
            }
        }

        private bool _handleCommodityRefinedEvent(CommodityRefinedEvent @event)
        {
            var update = false;
            var cargo = GetCargoWithEDName(@event.commodityDefinition?.edname);
            if (cargo != null)
            {
                var haulage = cargo.haulageData.FirstOrDefault(h => h.typeEDName
                    .ToLowerInvariant()
                    .Contains("mining"));
                if (haulage != null)
                {
                    haulage.sourcesystem = EDDI.Instance?.CurrentStarSystem?.systemname;
                    haulage.sourcebody = EDDI.Instance?.CurrentStation?.name;
                    update = true;
                }
            }
            return update;
        }

        private void handleCommoditySoldEvent(CommoditySoldEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                var cargo = GetCargoWithEDName(@event.commodityDefinition?.edname);
                if (cargo != null)
                {
                    // Flag event to check whether haulage was sold in following 'Cargo' event
                    checkHaulage = true;
                }
            }
        }

        private void handleCargoDepotEvent(CargoDepotEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                _handleCargoDepotEvent(@event);
                writeInventory();
            }
        }

        private void _handleCargoDepotEvent(CargoDepotEvent @event)
        {
            var mission = ConfigService.Instance.missionMonitorConfiguration
                ?.missions
                ?.FirstOrDefault(m => m.missionid == @event.missionid);
            Cargo cargo;
            Haulage haulage;
            var amountRemaining = @event.totaltodeliver - @event.delivered;

            switch (@event.updatetype)
            {
                case "Collect":
                    {
                        cargo = GetCargoWithMissionId(@event.missionid ?? 0);
                        if (cargo != null)
                        {
                            // Cargo instantiated by either 'Mission accepted' event or previous 'WingUpdate' update
                            haulage = cargo.haulageData.FirstOrDefault(ha => ha.missionid == @event.missionid);
                            if (haulage != null)
                            {
                                haulage.remaining = amountRemaining;
                                haulage.originsystem = EDDI.Instance?.CurrentStarSystem?.systemname;
                            }

                            // Update commodity definition if instantiated other than 'Mission accepted'
                            cargo.commodityDef = @event.commodityDefinition;
                        }
                        else
                        {
                            // First exposure to new cargo.
                            cargo = new Cargo(@event.commodityDefinition.edname); // Total will be updated by following 'Cargo' event
                            var originSystem = EDDI.Instance?.CurrentStarSystem?.systemname;
                            var name = mission?.name ?? "MISSION_DeliveryWing";
                            haulage = new Haulage(@event.missionid ?? 0, name, originSystem, amountRemaining, null, true);
                            cargo.haulageData.Add(haulage);
                        }
                        if (haulage != null)
                        {
                            haulage.collected = @event.collected;
                            haulage.delivered = @event.delivered;
                            haulage.startmarketid = @event.startmarketid;
                            haulage.endmarketid = @event.endmarketid;                            
                        }
                        AddOrUpdateCargo(cargo);
                    }
                    break;
                case "Deliver":
                    {
                        cargo = GetCargoWithMissionId(@event.missionid ?? 0);
                        if (cargo != null)
                        {
                            // Cargo instantiated by either 'Mission accepted' event, previous 'WingUpdate' or 'Collect' updates
                            haulage = cargo.haulageData.FirstOrDefault(ha => ha.missionid == @event.missionid);
                            if (haulage != null)
                            {
                                haulage.remaining = amountRemaining;
                                haulage.need = amountRemaining;

                                //Update commodity definition
                                haulage.amount = @event.totaltodeliver;
                                cargo.commodityDef = @event.commodityDefinition;
                                haulage.originsystem = (@event.startmarketid == 0) ? EDDI.Instance?.CurrentStarSystem?.systemname : null;
                            }
                            else
                            {
                                var originSystem = (@event.startmarketid == 0) ? EDDI.Instance?.CurrentStarSystem?.systemname : null;
                                var name = mission?.name ?? (@event.startmarketid == 0 ? "MISSION_CollectWing" : "MISSION_DeliveryWing");
                                haulage = new Haulage(@event.missionid ?? 0, name, originSystem, amountRemaining, null);
                                cargo.haulageData.Add(haulage);
                            }
                        }
                        else
                        {
                            // Check if cargo instantiated by previous 'Market buy' event
                            // Total will be updated by following 'Cargo' event
                            cargo = GetCargoWithEDName(@event.commodityDefinition.edname) ?? new Cargo(@event.commodityDefinition.edname); 
                            var originSystem = (@event.startmarketid == 0) ? EDDI.Instance?.CurrentStarSystem?.systemname : null;
                            var name = mission?.name ?? (@event.startmarketid == 0 ? "MISSION_CollectWing" : "MISSION_DeliveryWing");
                            haulage = new Haulage(@event.missionid ?? 0, name, originSystem, amountRemaining, null, true);
                            cargo.haulageData.Add(haulage);
                            AddOrUpdateCargo(cargo);
                        }

                        // Update 'Need' when cargo is delivered, as the 'Cargo' event handler does not update 'Collect' mission types
                        cargo.CalculateNeed();

                        haulage.collected = @event.collected;
                        haulage.delivered = @event.delivered;
                        haulage.endmarketid = (haulage.endmarketid == 0) ? @event.endmarketid : haulage.endmarketid;

                        // Check for mission completion
                        if (amountRemaining == 0)
                        {
                            if (haulage.shared)
                            {
                                cargo.haulageData.Remove(haulage);
                                RemoveCargo(cargo);
                            }
                            else
                            {
                                haulage.status = "Complete";
                            }
                        }
                    }
                    break;
                case "WingUpdate":
                    {
                        cargo = GetCargoWithMissionId(@event.missionid ?? 0);
                        if (cargo != null)
                        {
                            // Cargo instantiated by either 'Mission accepted' event, previous 'WingUpdate' or 'Collect' updates
                            haulage = cargo.haulageData.FirstOrDefault(ha => ha.missionid == @event.missionid);
                            if (haulage != null)
                            {
                                haulage.remaining = amountRemaining;
                                haulage.need = amountRemaining;                                
                            }
                        }
                        else
                        {
                            // First exposure to new cargo, use 'Unknown' as placeholder
                            cargo = new Cargo("Unknown");
                            var name = mission?.name ?? (@event.startmarketid == 0 ? "MISSION_CollectWing" : "MISSION_DeliveryWing");
                            haulage = new Haulage(@event.missionid ?? 0, name, null, amountRemaining, null, true);
                            cargo.haulageData.Add(haulage);
                        }
                        AddOrUpdateCargo(cargo);

                        // Generate a derived event when a wing-mate collects or delivers cargo for a wing mission
                        if (haulage != null)
                        {
                            var amount = Math.Max(@event.collected - haulage.collected, @event.delivered - haulage.delivered);
                            if (amount > 0)
                            {
                                var updatetype = @event.collected > haulage.collected ? "Collect" : "Deliver";
                                EDDI.Instance.enqueueEvent(new CargoWingUpdateEvent(DateTime.UtcNow, haulage.missionid, updatetype, cargo.commodityDef, amount, @event.collected, @event.delivered, @event.totaltodeliver));
                                haulage.collected = @event.collected;
                                haulage.delivered = @event.delivered;
                                haulage.startmarketid = @event.startmarketid;
                                haulage.endmarketid = @event.endmarketid;

                                // Update 'Need' when a wing-mate delivers cargo for a wing mission
                                if (updatetype == "Deliver") { cargo.CalculateNeed(); }
                            }

                            // Check for mission completion
                            if (amountRemaining == 0)
                            {
                                if (haulage.shared)
                                {
                                    cargo.haulageData.Remove(haulage);
                                    RemoveCargo(cargo);
                                }
                                else
                                {
                                    haulage.status = "Complete";
                                }
                            }
                        }
                    }
                    break;
            }
        }
        private void handleLimpetPurchasedEvent(LimpetPurchasedEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                if (_handleLimpetPurchasedEvent(@event))
                {
                    writeInventory();
                }
            }
        }

        private bool _handleLimpetPurchasedEvent(LimpetPurchasedEvent @event)
        {
            var cargo = GetCargoWithEDName("Drones") ?? new Cargo("Drones");
            cargo.AddDetailedQty(CargoType.owned, @event.amount, @event.price);
            AddOrUpdateCargo(cargo);
            return true;
        }

        private void handleMissionsEvent(MissionsEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                if (_handleMissionsEvent(@event))
                {
                    writeInventory();
                }
            }
        }

        private bool _handleMissionsEvent(MissionsEvent @event)
        {
            var update = false;
            foreach (var cargo in inventory.ToList())
            {
                // Strip out haulage strays
                foreach (var haulage in cargo.haulageData.ToList())
                {
                    var mission = @event.missions.FirstOrDefault(m => m.missionid == haulage.missionid);
                    if (mission == null)
                    {
                        cargo.haulageData.Remove(haulage);
                        update = true;
                    }
                }
            }
            return update;
        }

        private void handleMissionAbandonedEvent(MissionAbandonedEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                if (_handleMissionAbandonedEvent(@event))
                {
                    writeInventory();
                }
            }
        }

        private bool _handleMissionAbandonedEvent(MissionAbandonedEvent @event)
        {
            var update = false;
            var haulage = GetHaulageWithMissionId(@event.missionid ?? 0);
            if (haulage != null)
            {
                var cargo = GetCargoWithMissionId(@event.missionid ?? 0);
                var onboard = haulage.remaining - haulage.need;
                cargo.RemoveDetailedQty(CargoType.haulage, onboard, @event.missionid);
                cargo.AddDetailedQty(CargoType.stolen, onboard, cargo.price);
                RemoveCargo(cargo);
                update = true;
            }
            return update;
        }

        private void handleMissionAcceptedEvent(MissionAcceptedEvent @event)
        {
            if (@event.timestamp > updateDat && @event.Mission.CommodityDefinition != null)
            {
                updateDat = @event.timestamp;
                if (_handleMissionAcceptedEvent(@event))
                {
                    writeInventory();
                }
            }
        }

        private bool _handleMissionAcceptedEvent(MissionAcceptedEvent @event)
        {
            var update = false;

            var haulage = GetHaulageWithMissionId(@event.missionid ?? 0);
            if (haulage == null && !string.IsNullOrEmpty(@event.name))
            {
                var type = @event.name.Split('_').ElementAt(1)?.ToLowerInvariant();
                if (type != null && CHAINED.TryGetValue(type, out var value))
                {
                    type = value;
                }
                else if (type == "ds" || type == "rs" || type == "welcome")
                {
                    type = @event.name.Split('_').ElementAt(2)?.ToLowerInvariant();
                }

                switch (type)
                {
                    case "altruism":
                    case "collect":
                    case "collectwing":
                    case "delivery":
                    case "deliverywing":
                    case "mining":
                    case "piracy":
                    case "rescue":
                    case "salvage":
                    case "smuggle":
                        {
                            var naval = @event.name.ToLowerInvariant().Contains("rank");
                            var originSystem = EDDI.Instance?.CurrentStarSystem?.systemname;
                            haulage = new Haulage(@event.missionid ?? 0, @event.name, originSystem, @event.amount ?? 0, @event.expiry)
                            {
                                startmarketid = (type.Contains("delivery") && !naval) ? EDDI.Instance?.CurrentStation?.marketId ?? 0 : 0,
                                endmarketid = (type.Contains("collect")) ? EDDI.Instance?.CurrentStation?.marketId ?? 0 : 0,
                            };

                            if (type.Contains("delivery") || type == "smuggle")
                            {
                                haulage.sourcesystem = EDDI.Instance?.CurrentStarSystem?.systemname;
                                haulage.sourcebody = EDDI.Instance?.CurrentStation?.name;
                            }
                            else if (type == "rescue" || type == "salvage")
                            {
                                haulage.sourcesystem = @event.destinationsystem;
                            }

                            var cargo = GetCargoWithEDName(@event.Mission.CommodityDefinition?.edname) ?? new Cargo(@event.Mission.CommodityDefinition?.edname);
                            cargo.haulageData.Add(haulage);
                            cargo.CalculateNeed();
                            AddOrUpdateCargo(cargo);
                            update = true;
                        }
                        break;
                }
            }
            return update;
        }

        private void handleMissionCompletedEvent(MissionCompletedEvent @event)
        {
            if (@event.commodityDefinition != null || @event.commodityrewards != null)
            {
                if (@event.timestamp > updateDat)
                {
                    updateDat = @event.timestamp;
                    if (_handleMissionCompletedEvent(@event))
                    {
                        writeInventory();
                    }
                }
            }
        }

        private bool _handleMissionCompletedEvent(MissionCompletedEvent @event)
        {
            var update = false;
            var cargo = GetCargoWithEDName(@event.commodityDefinition?.edname);
            if (cargo != null)
            {
                var haulage = cargo.haulageData.FirstOrDefault(ha => ha.missionid == @event.missionid);
                if (haulage != null)
                {
                    cargo.haulageData.Remove(haulage);
                }
                RemoveCargo(cargo);
                update = true;
            }
            return update;
        }

        private void handleMissionExpiredEvent(MissionExpiredEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                if (_handleMissionExpiredEvent(@event))
                {
                    writeInventory();
                }
            }
        }

        private bool _handleMissionExpiredEvent(MissionExpiredEvent @event)
        {
            var update = false;
            var haulage = GetHaulageWithMissionId(@event.missionid ?? 0);
            if (haulage != null)
            {
                haulage.status = "Failed";
                update = true;
            }
            return update;
        }

        private void handleMissionFailedEvent(MissionFailedEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                if (_handleMissionFailedEvent(@event))
                {
                    writeInventory();
                }
            }
        }

        private bool _handleMissionFailedEvent(MissionFailedEvent @event)
        {
            var update = false;
            var haulage = GetHaulageWithMissionId(@event.missionid ?? 0);
            if (haulage != null)
            {
                var cargo = GetCargoWithMissionId(@event.missionid ?? 0);
                var onboard = haulage.remaining - haulage.need;
                cargo.RemoveDetailedQty(CargoType.haulage, onboard, haulage);
                cargo.AddDetailedQty(CargoType.stolen, onboard, cargo.price);
                RemoveCargo(cargo);
                return true;
            }
            return update;
        }

        private void handleDiedEvent()
        {
            inventory.Clear();
            writeInventory();
        }

        private void handleEngineerContributedEvent(EngineerContributedEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                if (_handleEngineerContributedEvent(@event))
                {
                    writeInventory();
                }
            }
        }

        private bool _handleEngineerContributedEvent(EngineerContributedEvent @event)
        {
            var update = false;
            if (@event.commodityAmount != null)
            {
                var cargo = GetCargoWithEDName(@event.commodityAmount.edname);
                if (cargo != null)
                {
                    cargo.RemoveDetailedQty(CargoType.owned, Math.Min(cargo.owned, @event.commodityAmount.amount));
                    RemoveCargo(cargo);
                    update = true;
                }
            }
            return update;
        }

        private void handleSynthesisedEvent(SynthesisedEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                if (_handleSynthesisedEvent(@event))
                {
                    writeInventory();
                }
            }
        }

        private bool _handleSynthesisedEvent(SynthesisedEvent @event)
        {
            if (@event.synthesis.Contains("Limpet"))
            {
                var cargo = GetCargoWithEDName("Drones") ?? new Cargo("Drones");
                cargo.AddDetailedQty(CargoType.owned, 4, 0);
                AddOrUpdateCargo(cargo);
                return true;
            }
            return false;
        }

        public IDictionary<string, Tuple<Type, object>> GetVariables ()
        {
            lock ( inventoryLock )
            {
                return new Dictionary<string, Tuple<Type, object>>
                {
                    ["inventory"] = new Tuple<Type, object>(typeof(List<Cargo>), inventory.ToList() ),
                    ["cargoCarried"] = new Tuple<Type, object>(typeof(int), cargoCarried)
                };                
            }
        }

        public void writeInventory()
        {
            lock (inventoryLock)
            {
                // Write cargo configuration with current inventory
                var configuration = new CargoMonitorConfiguration()
                {
                    updatedat = updateDat,
                    cargo = inventory,
                    cargocarried = cargoCarried
                };
                ConfigService.Instance.cargoMonitorConfiguration = configuration;
            }
            // Make sure the UI is up to date
            RaiseOnUIThread(InventoryUpdatedEvent, inventory);
        }

        private void readInventory(CargoMonitorConfiguration configuration = null)
        {
            lock (inventoryLock)
            {
                // Obtain current cargo inventory from configuration
                configuration = configuration ?? ConfigService.Instance.cargoMonitorConfiguration;
                cargoCarried = configuration.cargocarried;
                updateDat = configuration.updatedat;

                // Build a new inventory
                var newInventory = new List<Cargo>();

                // Start with the materials we have in the log
                foreach (var cargo in configuration.cargo)
                {
                    if (cargo.commodityDef == null)
                    {
                        cargo.commodityDef = CommodityDefinition.FromEDName(cargo.edname);
                    }
                    cargo.CalculateNeed();
                    newInventory.Add(cargo);
                }

                // Now order the list by name
                newInventory = newInventory.OrderBy(c => c.invariantName).ToList();

                // Update the inventory 
                inventory.Clear();
                foreach (var cargo in newInventory)
                {
                    inventory.Add(cargo);
                }
            }
        }

        private void AddOrUpdateCargo(Cargo cargo)
        {
            if (cargo == null) { return; }
            lock (inventoryLock)
            {
                var found = false;
                for (var i = 0; i < inventory.Count; i++)
                {
                    if (string.Equals(inventory[i].edname, cargo.edname, StringComparison.InvariantCultureIgnoreCase))
                    {
                        found = true;
                        inventory[i] = cargo;
                        break;
                    }
                }
                if (!found)
                {
                    inventory.Add(cargo);
                }
            }
        }

        private void RemoveCargo(Cargo cargo)
        {
            // Check if missions are pending
            if (cargo.haulageData == null || !cargo.haulageData.Any())
            {
                if (cargo.total < 1)
                {
                    // All of the commodity was either expended, ejected, or sold
                    _RemoveCargoWithEDName(cargo.edname);
                }
            }
            else
            {
                cargo.CalculateNeed();
            }
        }

        private void _RemoveCargoWithEDName(string edname)
        {
            lock (inventoryLock)
            {
                if (edname != null)
                {
                    edname = edname.ToLowerInvariant();
                    for (var i = 0; i < inventory.Count; i++)
                    {
                        if (inventory[i].edname.ToLowerInvariant() == edname)
                        {
                            inventory.RemoveAt(i);
                            break;
                        }
                    }
                }
            }
        }

        public Cargo GetCargoWithEDName(string edname)
        {
            if (edname == null)
            {
                return null;
            }
            edname = edname.ToLowerInvariant();
            return inventory.FirstOrDefault(c => c.edname.ToLowerInvariant() == edname);
        }

        public Cargo GetCargoWithMissionId(long missionid)
        {
            foreach (var cargo in inventory.ToList())
            {
                if (cargo.haulageData.FirstOrDefault(h => h.missionid == missionid) != null)
                {
                    return cargo;
                }
            }
            return null;
        }

        public Haulage GetHaulageWithMissionId(long missionid)
        {
            foreach (var cargo in inventory.ToList())
            {
                var haulage = cargo.haulageData.FirstOrDefault(h => h.missionid == missionid);
                if (haulage != null)
                {
                    return haulage;
                }
            }
            return null;
        }

        private void UpdateCargoFromInfo(Cargo cargo, List<CargoInfoItem> infoList)
        {
            cargo.haulage = infoList.Where(i => i.missionid != null).Sum(i => i.count);
            cargo.stolen = infoList.Where(i => i.missionid == null).Sum(i => i.stolen);
            cargo.owned = infoList.Sum(i => i.count) - cargo.haulage - cargo.stolen;

            foreach (var info in infoList.Where(i => i.missionid != null).ToList())
            {
                var mission = ConfigService.Instance.missionMonitorConfiguration
                    ?.missions
                    ?.FirstOrDefault(m => m.missionid == info.missionid);
                var cargoHaulage = cargo.haulageData.FirstOrDefault(h => h.missionid == info.missionid);
                if (cargoHaulage != null)
                {
                    // Check for sold haulage
                    if (checkHaulage && cargoHaulage.need > info.count)
                    {
                        // We lost haulage
                        switch (cargoHaulage.typeEDName)
                        {
                            case "delivery":
                            case "deliverywing":
                            case "smuggle":
                                {
                                    cargoHaulage.status = "Failed";
                                    if (mission != null)
                                    {
                                        mission.statusDef = MissionStatus.Failed;
                                    }
                                }
                                break;
                        }
                    }
                }
                else
                {
                    var name = mission?.name ?? "Mission_None";
                    var amount = mission?.amount ?? info.count;
                    var expiry = mission?.expiry;

                    cargoHaulage = new Haulage(info.missionid ?? 0, name, mission?.originsystem, amount, expiry);
                    cargo.haulageData.Add(cargoHaulage);
                }
            }
            cargo.CalculateNeed();
            checkHaulage = false;
        }

        static void RaiseOnUIThread(EventHandler handler, object sender)
        {
            if (handler != null)
            {
                var uiSyncContext = SynchronizationContext.Current ?? new SynchronizationContext();
                if (uiSyncContext == null)
                {
                    handler(sender, EventArgs.Empty);
                }
                else
                {
                    uiSyncContext.Send(delegate { handler(sender, EventArgs.Empty); }, null);
                }
            }
        }
    }
}
