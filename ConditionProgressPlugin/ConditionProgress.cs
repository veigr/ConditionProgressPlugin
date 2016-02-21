using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using System.Windows.Shell;
using System.Windows.Threading;
using Grabacr07.KanColleViewer.Composition;
using Grabacr07.KanColleWrapper;
using MetroTrilithon.Lifetime;
using MetroTrilithon.Linq;
using MetroTrilithon.Mvvm;
using StatefulModel;
using Grabacr07.KanColleWrapper.Models;

namespace ConditionProgressPlugin
{
    [Export(typeof(IPlugin))]
    [Export(typeof(ITaskbarProgress))]
    [ExportMetadata("Guid", guid)]
    [ExportMetadata("Title", "ConditionProgress")]
    [ExportMetadata("Description", "第1艦隊 or 連合艦隊の疲労回復状況をタスク バー インジケーターに報告します。")]
    [ExportMetadata("Version", "1.0.0")]
    [ExportMetadata("Author", "@veigr")]
    public class ConditionProgress : IPlugin, ITaskbarProgress, IDisposableHolder
    {
        private const string guid = "DDF8D95C-0225-47A1-B7C8-ACD9C7E5AC11";

        private readonly MultipleDisposable compositDisposable = new MultipleDisposable();
        private readonly List<IDisposable> fleetHandlers = new List<IDisposable>();
        private bool initialized;

        public string Id => guid + "-1";

        public string DisplayName => "疲労回復";

        public TaskbarItemProgressState State { get; private set; }

        public double Value { get; private set; }

        public event EventHandler Updated;

        public void Initialize()
        {
            KanColleClient.Current
                .Subscribe(nameof(KanColleClient.IsStarted), () => this.InitializeCore(), false)
                .AddTo(this);
        }

        private void InitializeCore()
        {
            if (this.initialized) return;

            var homeport = KanColleClient.Current.Homeport;
            if (homeport == null) return;

            this.initialized = true;
            homeport.Organization
                .Subscribe(nameof(Organization.Fleets), this.UpdateFleets)
                .Subscribe(nameof(Organization.CombinedFleet), this.UpdateFleets)
                .AddTo(this);
        }

        public void UpdateFleets()
        {
            if (!this.initialized) return;

            foreach (var handler in this.fleetHandlers)
            {
                handler.Dispose();
            }
            this.fleetHandlers.Clear();

            var state = GetCurrentFleetStaete();
            if (state == null) return;
            this.fleetHandlers.Add(state.Condition.Subscribe(nameof(FleetCondition.Remaining), this.Update));
        }

        public void Update()
        {
            var state = GetCurrentFleetStaete();
            var remaining = state.Condition.Remaining;

            if (!remaining.HasValue)
            {
                this.State = TaskbarItemProgressState.None;
                this.Value = .0;
            }
            else if (remaining.Value <= TimeSpan.Zero)
            {
                this.State = TaskbarItemProgressState.Indeterminate;
                this.Value = 1.0;
            }
            else
            {
                this.State = TaskbarItemProgressState.Normal;
                var minCond = Math.Min(49, GetCurrentTargets().Min(x => x.Condition));
                var rejuvnate = (int)Math.Ceiling((49m - minCond) / 3) * 3 * 60;
                this.Value = 1.0 - remaining.Value.TotalSeconds / rejuvnate;
            }

            this.Updated?.Invoke(this, EventArgs.Empty);
        }

        private static IEnumerable<Ship> GetCurrentTargets()
        {
            var org = KanColleClient.Current.Homeport.Organization;
            return org.Combined
                ? org.CombinedFleet.Fleets.SelectMany(x => x.Ships)
                : org.Fleets.Values.FirstOrDefault()?.Ships;
        }

        private static FleetState GetCurrentFleetStaete()
        {
            var org = KanColleClient.Current.Homeport.Organization;
            return org.Combined
                ? org.CombinedFleet?.State
                : org.Fleets.Values.FirstOrDefault()?.State;
        }

        public void Dispose() => this.compositDisposable.Dispose();
        ICollection<IDisposable> IDisposableHolder.CompositeDisposable => this.compositDisposable;
    }
}
