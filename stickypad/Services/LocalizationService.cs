using System.Globalization;
using System.Threading;

namespace StickyPad.Services;

public static class LocalizationService
{
    /// 설정 언어("system"/"en"/"ko")에 따라 현재 스레드/기본 스레드 컬처를 지정한다.
    /// 앱 시작 시 창을 만들기 전에 호출해야 한다.
    public static void ApplyCulture(string? language)
    {
        CultureInfo culture = language switch
        {
            "en" => new CultureInfo("en"),
            "ko" => new CultureInfo("ko"),
            _ => CultureInfo.CurrentUICulture, // system: OS 언어 사용(ko 아니면 영어 폴백)
        };
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
    }
}
