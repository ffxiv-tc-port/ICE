using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace ICE.Scheduler.Handlers
{
    internal class WeatherForecastHandler
    {
        private const double Seconds = 1;
        private const double Minutes = 60 * Seconds;
        private const double WeatherPeriod = 23 * Minutes + 20 * Seconds;
        internal static bool AccurateTime = false;

        internal static List<WeatherForecast> weathers = [];

        // 上次建 weathers 時的輸入：區域、期起點（整秒）、目前天氣＋未來 24 期的天氣 id；forecastOffsets[k]＝weathers[k] 的期偏移。
        private static ushort forecastTerritory;
        private static long forecastPeriodStart = long.MinValue;
        private static readonly byte[] forecastIds = new byte[25];
        private static readonly int[] forecastOffsets = new int[25];

        // 上游這裡的提早返回條件恆不成立（NeedToRefreshBecauseRedAlert 初值就是 true）＝每幀重算；
        // 這裡照樣每幀重算，輸入沒變就沿用上一份清單（見 GetForecast）。
        internal static void Tick() => RefreshForecast();

        internal static void RefreshForecast()
        {
            if (!PlayerHelper.IsInCosmicZone() || WeatherFunctionsMissing()) return;

            GetForecast();
        }

        // 這裡每幀都會走到（在宇宙區域時）；位址沒解出時 CS 包裝會擲例外，所以先問。
        private static unsafe bool WeatherFunctionsMissing()
            => GameFunctionGuard.Missing(WeatherManager.MemberFunctionPointers.GetCurrentWeather is null
                                         || WeatherManager.MemberFunctionPointers.GetWeatherForDaytime is null,
                                         "WeatherManager（天氣查詢）", "天氣預報不會顯示。");

        private static unsafe Weather GetCurrentWeather()
        {
            WeatherManager* wm = WeatherManager.Instance();
            byte currWeatherId = wm->GetCurrentWeather();
            return ExcelHelper.WeatherSheet.GetRow(currWeatherId);
        }

        internal static unsafe uint GetCurrentWeatherId()
        {
            if (!PlayerHelper.IsInCosmicZone() || WeatherFunctionsMissing()) return default;

            Weather currWeather = GetCurrentWeather();
            return currWeather.RowId;
        }

        internal static unsafe (string, uint, string, uint, string) GetNextWeather()
        {
            if (!PlayerHelper.IsInCosmicZone()) return default;
            // 只有一筆（預報期內天氣不變）時沒有「下一個天氣」可回
            if (weathers.Count < 2) return default;

            var currentWeather = weathers[0];
            var nextWeather = weathers[1];

            return (currentWeather.Name, currentWeather.IconId, nextWeather.Name, nextWeather.IconId, FormatForecastTime(nextWeather.Time));
        }

        internal static unsafe void GetForecast()
        {
            if (WeatherFunctionsMissing()) return;

            var territory = Svc.ClientState.TerritoryType;
            WeatherManager* wm = WeatherManager.Instance();
            Span<byte> ids = stackalloc byte[25];
            ids[0] = wm->GetCurrentWeather();
            for (var i = 1; i <= 24; i++)
                ids[i] = wm->GetWeatherForDaytime(territory, i);

            // 契約：輸入與建清單時全同 ⇒ 天氣與筆數不變，只照重建的算法就地刷新 Time，結果與每幀重建相同。
            var periodStart = PeriodStartSecond();
            if (territory == forecastTerritory && periodStart == forecastPeriodStart && ids.SequenceEqual(forecastIds))
            {
                for (var k = 0; k < weathers.Count; k++)
                    weathers[k].Time = GetRootTime(forecastOffsets[k] * WeatherPeriod);
                return;
            }

            // 先作廢：建到一半擲例外時下一幀照舊重建。
            forecastPeriodStart = long.MinValue;

            Weather currentWeather = ExcelHelper.WeatherSheet.GetRow(ids[0]);
            Weather lastWeather = currentWeather;

            weathers = [BuildResultObject(currentWeather, GetRootTime(0))];
            forecastOffsets[0] = 0;

            for (var i = 1; i <= 24; i++)
            {
                var weather = ExcelHelper.WeatherSheet.GetRow(ids[i])!;
                var time = GetRootTime(i * WeatherPeriod);

                if (lastWeather.RowId != weather.RowId)
                {
                    lastWeather = weather;
                    weathers.Add(BuildResultObject(weather, time));
                    forecastOffsets[weathers.Count - 1] = i;
                }
            }

            // 建到一半跨過期界就不記輸入，下一幀照舊重建。
            if (PeriodStartSecond() != periodStart) return;
            forecastTerritory = territory;
            forecastPeriodStart = periodStart;
            ids.CopyTo(forecastIds);
        }

        private static long PeriodStartSecond() => GetRootTime(0).Ticks / TimeSpan.TicksPerSecond;

        internal static unsafe List<WeatherForecast> GetTerritoryForecast(ushort territoryId)
        {
            List<WeatherForecast> territoryForecast = [];
            if (WeatherFunctionsMissing()) return territoryForecast;

            WeatherManager* wm = WeatherManager.Instance();
            byte weatherId = wm->GetWeatherForDaytime(territoryId, 0);
            Weather currentWeather = ExcelHelper.WeatherSheet.GetRow(weatherId);
            Weather lastWeather = currentWeather;

            territoryForecast = [BuildResultObject(currentWeather, GetRootTime(0))];

            for (var i = 1; i <= 24; i++)
            {
                weatherId = wm->GetWeatherForDaytime(territoryId, i);
                var weather = ExcelHelper.WeatherSheet.GetRow(weatherId)!;
                var time = GetRootTime(i * WeatherPeriod);

                if (lastWeather.RowId != weather.RowId)
                {
                    lastWeather = weather;
                    territoryForecast.Add(BuildResultObject(weather, time));
                }
            }
            return territoryForecast;
        }
        private static WeatherForecast BuildResultObject(Weather weather, DateTime time)
        {
            var name = weather.Name.ExtractText();
            var iconId = (uint)weather.Icon;

            return new(time, name, iconId);
        }
        private static DateTime GetRootTime(double initialOffset)
        {
            var now = DateTime.UtcNow;
            var rootTime = now.AddMilliseconds(-now.Millisecond).AddSeconds(initialOffset);
            var seconds = (long)(rootTime - DateTime.UnixEpoch).TotalSeconds % WeatherPeriod;

            rootTime = rootTime.AddSeconds(-seconds);

            return rootTime;
        }

        internal static string FormatForecastTime(DateTime forecastTime)
        {
            TimeSpan timeDifference = forecastTime - DateTime.UtcNow;
            if (!AccurateTime)
            {
                string format = C.ShowSeconds ? @"hh\:mm\:ss" : @"hh\:mm";
                return timeDifference < TimeSpan.Zero ? "-" + timeDifference.Duration().ToString(format) : timeDifference.ToString(format);
            }
            else
            {
                int totalSeconds = Math.Abs((int)timeDifference.TotalSeconds);
                int hours = totalSeconds / 10000;
                int minutes = (totalSeconds % 10000) / 100;
                string format = C.ShowSeconds ? $"{hours:D2}:{minutes:D2}:{totalSeconds % 100:D2}" : $"{hours:D2}:{minutes:D2}";
                return timeDifference < TimeSpan.Zero ? "-" + format : format;
            }
        }
    }

    internal class WeatherForecast(DateTime time, string name, uint iconId)
    {
        public DateTime Time = time;
        public string Name = name;
        public uint IconId = iconId;
    }
}
