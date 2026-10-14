using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BCOpendayApp.Services
{
    public interface IAnalyticsService
    {
        void LogScreenView(string screenName);
        void LogHotspotTapped(string hotspotId, string hotspotName);
        void LogLocationOpened(string locationId, string locationName);
        void LogError(string area, Exception exception);
        void TestCrashlytics();
    }
}