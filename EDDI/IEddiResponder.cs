﻿using EddiEvents;
using System.AddIn.Pipeline;
using System.Windows.Controls;

namespace EddiCore
{
    /// <summary>
    /// The methods required for an EDDI responder.
    /// </summary>
    [AddInContract]
    public interface IEddiResponder
    {
        /// <summary>
        /// A short name for the responder
        /// </summary>
        string ResponderName();

        /// <summary>
        /// A localized name for the responder
        /// </summary>
        string LocalizedResponderName();

        /// <summary>
        /// A brief description of the responder
        /// </summary>
        string ResponderDescription();

        /// <summary>
        /// Called when this responder is started; time to carry out initialisation
        /// </summary>
        /// <returns>true if the responder has started successfully; otherwise false</returns>
        bool Start();

        /// <summary>
        /// Called when this responder is stopped; time to shut down daemons and similar
        /// </summary>
        void Stop();

        /// <summary>
        /// Called when this responder needs to reload its configuration
        /// </summary>
        void Reload();

        /// <summary>
        /// Called when an event is found. Must not change global states.
        /// </summary>
        void Handle(Event theEvent);

        UserControl ConfigurationTabItem();
    }
}
