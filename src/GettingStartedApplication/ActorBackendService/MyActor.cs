﻿// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace ActorBackendService
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;    
    using Microsoft.ApplicationInsights;
    using Microsoft.ApplicationInsights.DataContracts;
    using Microsoft.ApplicationInsights.Extensibility;
    using Microsoft.ApplicationInsights.ServiceFabric;
    using Microsoft.Diagnostics.Activities;
    using Microsoft.ServiceFabric.Actors;
    using Microsoft.ServiceFabric.Actors.Runtime;
    using ActorBackendService.Interfaces;

    /// <remarks>
    /// This class represents an actor.
    /// Every ActorID maps to an instance of this class.
    /// The StatePersistence attribute determines persistence and replication of actor state:
    ///  - Persisted: State is written to disk and replicated.
    ///  - Volatile: State is kept in memory only and replicated.
    ///  - None: State is kept in memory only and not replicated.
    /// </remarks>
    [StatePersistence(StatePersistence.Persisted)]
    internal class MyActor : Actor, IMyActor, IRemindable
    {
        private const string ReminderName = "Reminder";
        private const string StateName = "Count";
        private TelemetryClient telemetryClient;

        /// <summary>
        /// Initializes a new instance of ActorBackendService
        /// </summary>
        /// <param name="actorService">The Microsoft.ServiceFabric.Actors.Runtime.ActorService that will host this actor instance.</param>
        /// <param name="actorId">The Microsoft.ServiceFabric.Actors.ActorId for this actor instance.</param>
        public MyActor(ActorService actorService, ActorId actorId)
            : base(actorService, actorId)
        {
            var telemetryConfig = TelemetryConfiguration.Active;
            FabricTelemetryInitializerExtension.SetServiceCallContext(actorService.Context);
            var config = actorService.Context.CodePackageActivationContext.GetConfigurationPackageObject("Config");
            var appInsights = config.Settings.Sections["ApplicationInsights"];
            telemetryConfig.InstrumentationKey = appInsights.Parameters["InstrumentationKey"].Value;

            telemetryClient = new TelemetryClient(TelemetryConfiguration.Active);
        }

        public Task StartProcessingAsync(string requestId, IEnumerable<KeyValuePair<string, string>> correlationContextHeader, CancellationToken cancellationToken)
        {
            return Activities.HandleActorRequestAsync(async () =>
            {
                try
                {
                    this.GetReminder(ReminderName);
                }
                catch (ReminderNotFoundException)
                {
                    await this.RegisterReminderAsync(ReminderName, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10));
                }

                bool added = await this.StateManager.TryAddStateAsync<long>(StateName, 0);
                if (!added)
                {
                    // value already exists, which means processing has already started.
                    throw new InvalidOperationException("Processing for this actor has already started.");
                }
            }, requestId, requestName: "fabric:/GettingStartedApplication/ActorBackendService/StartProcessingAsync", correlationContext: correlationContextHeader);
        }

        public async Task ReceiveReminderAsync(string reminderName, byte[] context, TimeSpan dueTime, TimeSpan period)
        {
            if (reminderName.Equals(ReminderName, StringComparison.OrdinalIgnoreCase))
            {
                long currentValue = await this.StateManager.GetStateAsync<long>(StateName);

                ActorEventSource.Current.ActorMessage(this, $"Processing. Current value: {currentValue}");

                await this.StateManager.SetStateAsync<long>(StateName, ++currentValue);
            }
        }

        /// <summary>
        /// This method is called whenever an actor is activated.
        /// An actor is activated the first time any of its methods are invoked.
        /// </summary>
        protected override async Task OnActivateAsync()
        {
            ActorEventSource.Current.ActorMessage(this, "Actor activated.");

            // The StateManager is this actor's private state store.
            // Data stored in the StateManager will be replicated for high-availability for actors that use volatile or persisted state storage.
            // Any serializable object can be saved in the StateManager.
            // For more information, see https://aka.ms/servicefabricactorsstateserialization
            await base.OnActivateAsync();
        }
    }
}