﻿using Cottle;
using Cottle.Builtins;
using Cottle.Documents;
using Cottle.Settings;
using Cottle.Stores;
using Cottle.Values;
using Eddi;
using EddiBgsService;
using EddiCompanionAppService;
using EddiCore;
using EddiDataProviderService;
using EddiEvents;
using EddiNavigationService;
using EddiSpeechService;
using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Utilities;

namespace EddiSpeechResponder.Service
{
    public class ScriptResolver
    {
        private readonly Dictionary<string, Script> scripts;
        private readonly CustomSetting setting;
        internal readonly Random random;
        internal readonly DataProviderService dataProviderService;
        internal readonly BgsService bgsService;

        // The file to log speech
        [UsedImplicitly] public static readonly string LogFile = Constants.DATA_DIR + @"\speechresponder.out";

        public ScriptResolver(Dictionary<string, Script> scripts)
        {
            dataProviderService = new DataProviderService();
            bgsService = new BgsService();
            random = new Random();
            this.scripts = scripts ?? new Dictionary<string, Script>();
            setting = new CustomSetting
            {
                Trimmer = BuiltinTrimmers.CollapseBlankCharacters
            };
        }

        public int priority(string name)
        {
            scripts.TryGetValue(name, out Script script);
            return script?.Priority ?? 3;
        }

        /// <summary> From a custom dictionary of variable values in the default store </summary>
        public string resolveFromName(string name, Dictionary<string, object> vars, bool isTopLevelScript)
        {
            BuiltinStore store = buildStore(vars);
            return resolveFromName(name, store, isTopLevelScript);
        }

        /// <summary> From a custom store </summary>
        public string resolveFromName(string name, BuiltinStore store, bool isTopLevelScript)
        {
            if (!scripts.TryGetValue(name, out Script script) || 
                script?.Value is null)
            {
                Logging.Debug($"No {name} script found");
                return null;
            }
            if (script.Enabled == false)
            {
                Logging.Debug($"{name} script disabled");
                return null;
            }

            return resolveFromValue(script.Value, store, isTopLevelScript, script);
        }

        /// <summary> From the default dictionary of variable values in the default store </summary>
        public string resolveFromValue(string scriptValue, bool isTopLevelScript)
        {
            var vars = CompileVariables();
            BuiltinStore store = buildStore(vars);
            return resolveFromValue(scriptValue, store, isTopLevelScript);
        }

        /// <summary> From a custom store </summary>
        public string resolveFromValue(string script, BuiltinStore store, bool isTopLevelScript, Script scriptObject = null)
        {
            try
            {
                Logging.Debug($"Resolving {(isTopLevelScript ? "top level " : "")}script {scriptObject?.Name}: {script}", store);

                var document = new SimpleDocument(script, setting);
                var result = document.Render(store);
                // Tidy up the output script
                if (isTopLevelScript)
                {
                    result = Regex.Replace(result, " +", " ").Replace(" ,", ",").Replace(" .", ".").Trim();
                    Logging.Debug($"Turned {scriptObject?.Name} script into speech '{result}'");
                    result = result.Trim() == "" ? null : result.Trim();
                }

                if (isTopLevelScript && result != null)
                {
                    string stored = result;
                    // Remove any leading pause
                    if (stored.StartsWith("<break"))
                    {
                        string pattern = "^<break[^>]*>";
                        string replacement = "";
                        Regex rgx = new Regex(pattern);
                        stored = rgx.Replace(stored, replacement);
                    }
                    EDDI.Instance.State["eddi_context_last_speech"] = stored;
                }

                return result;
            }
            catch (Cottle.Exceptions.ParseException e)
            {
                // Report the failing the script name, if it is available
                string scriptName;
                if (scriptObject != null)
                {
                    scriptName = "the script \"" + scriptObject.Name + "\"";
                }
                else
                {
                    scriptName = "this script";
                }

                Logging.Warn($"Failed to resolve {scriptName} at line {e.Line}. {e}");
                return $"There is a problem with {scriptName} at line {e.Line}. {errorTranslation(e.Message)}";
            }
            catch (Exception e)
            {
                Logging.Error(e.Message, e);
                return $"Error with {scriptObject?.Name ?? "this"} script: {e.Message}";
            }
        }

        private static string errorTranslation(string msg)
        {
            // Give human readable descriptions for select cottle errors
            return msg
                    .Replace("'", "")
                    .Replace("<EOF>", "opening curly bracket")
                    .Replace("<eof>", "incomplete expression")
                    .Replace("{", "opening curly bracket")
                    .Replace("}", "closing curly bracket");
        }

        // Compile variables from the EDDI information
        protected internal Dictionary<string, object> CompileVariables(Event theEvent = null)
        {
            var dict = new Dictionary<string, object>
            {
                ["capi_active"] = CompanionAppService.Instance?.active ?? false,
                ["destinationdistance"] = EDDI.Instance.DestinationDistanceLy,
                ["searchdistance"] = NavigationService.Instance.SearchDistanceLy,
                ["environment"] = EDDI.Instance.Environment,
                ["horizons"] = EDDI.Instance.inHorizons,
                ["odyssey"] = EDDI.Instance.inOdyssey,
                ["va_active"] = App.FromVA,
                ["vehicle"] = EDDI.Instance.Vehicle,
                ["icao_active"] = SpeechService.Instance.Configuration.EnableIcao,
                ["ipa_active"] = !SpeechService.Instance.Configuration.DisableIpa,

                // Boolean constants
                ["true"] = true,
                ["false"] = false
            };

            if (EDDI.Instance.Cmdr != null)
            {
                dict["cmdr"] = EDDI.Instance.Cmdr;
            }

            if (EDDI.Instance.HomeStarSystem != null)
            {
                dict["homesystem"] = EDDI.Instance.HomeStarSystem;
            }

            if (EDDI.Instance.HomeStation != null)
            {
                dict["homestation"] = EDDI.Instance.HomeStation;
            }

            if (EDDI.Instance.SquadronStarSystem != null)
            {
                dict["squadronsystem"] = EDDI.Instance.SquadronStarSystem;
            }

            if (EDDI.Instance.CurrentStarSystem != null)
            {
                dict["system"] = EDDI.Instance.CurrentStarSystem;
            }

            if (EDDI.Instance.LastStarSystem != null)
            {
                dict["lastsystem"] = EDDI.Instance.LastStarSystem;
            }

            if (EDDI.Instance.NextStarSystem != null)
            {
                dict["nextsystem"] = EDDI.Instance.NextStarSystem;
            }

            if (EDDI.Instance.DestinationStarSystem != null)
            {
                dict["destinationsystem"] = EDDI.Instance.DestinationStarSystem;
            }

            if (NavigationService.Instance.SearchStarSystem != null)
            {
                dict["searchsystem"] = NavigationService.Instance.SearchStarSystem;
            }

            if (NavigationService.Instance.SearchStation != null)
            {
                dict["searchstation"] = NavigationService.Instance.SearchStation;
            }
            
            if (EDDI.Instance.CurrentStation != null)
            {
                dict["station"] = EDDI.Instance.CurrentStation;
            }

            if (EDDI.Instance.CurrentStellarBody != null)
            {
                dict["body"] = EDDI.Instance.CurrentStellarBody;
            }

            if (EDDI.Instance.FleetCarrier != null)
            {
                dict["carrier"] = EDDI.Instance.FleetCarrier;
            }

            if (theEvent != null)
            {
                dict["event"] = theEvent;
            }

            if (EDDI.Instance.State != null)
            {
                dict["state"] = EDDI.Instance.State;
                Logging.Debug("State is: ", EDDI.Instance.State);
            }

            // Obtain additional variables from each monitor
            foreach (IEddiMonitor monitor in EDDI.Instance.monitors)
            {
                IDictionary<string, object> monitorVariables = monitor.GetVariables();
                if (monitorVariables != null)
                {
                    foreach (string key in monitorVariables.Keys)
                    {
                        if (monitorVariables[key] == null)
                        {
                            dict.Remove(key);
                        }
                        else
                        {
                            dict[ key ] = monitorVariables[ key ];
                        }
                    }
                }
            }

            return dict;
        }

        /// <summary>
        /// Build a store from a list of variables
        /// </summary>
        public BuiltinStore buildStore(Dictionary<string, object> vars = null)
        {
            BuiltinStore store = new BuiltinStore();
            
            // Loop through our custom functions and add them to the store.
            foreach (var type in Assembly.GetExecutingAssembly().GetTypes()
                .Where(t => t.IsClass && t.GetInterface(nameof(ICustomFunction)) != null))
            {
                // Create an instance of the function and add it to the store,
                // either with no .ctor or passing data to a .ctor as required (for classes implementing `CustomNestingFunction`).
                var function = (ICustomFunction)(type.GetConstructor(Type.EmptyTypes) != null 
                    ? Activator.CreateInstance(type) : 
                    Activator.CreateInstance(type, this, store)); 
                store.Set(function.name, function.function, StoreMode.Global);
            }

            // Variables
            if (vars != null)
            {
                foreach (var entry in vars)
                {
                    if ( entry.Value is null )
                    {
                        store[ entry.Key ] = new VoidValue();
                    }
                    else
                    {
                        store[ entry.Key ] = new ReflectionValue( entry.Value );
                    }
                }
            }

            return store;
        }

        public static Dictionary<Cottle.Value, Cottle.Value> buildState()
        {
            if (EDDI.Instance.State == null)
            {
                return null;
            }

            var state = new Dictionary<Cottle.Value, Cottle.Value>();
            foreach (string key in EDDI.Instance.State.Keys)
            {
                object value = EDDI.Instance.State[key];
                if (value == null)
                {
                    // Null values should not be included in our Cottle state
                    continue;
                }
                Type valueType = value.GetType();
                if (valueType == typeof(string))
                {
                    state[key] = (string)value;
                }
                else if (valueType == typeof(int))
                {
                    state[key] = (int)value;
                }
                else if (valueType == typeof(bool))
                {
                    state[key] = (bool)value;
                }
                else if (valueType == typeof(decimal))
                {
                    state[key] = (decimal)value;
                }
            }
            return state;
        }
    }
}
