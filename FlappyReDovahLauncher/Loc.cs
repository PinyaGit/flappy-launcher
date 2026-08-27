using System;
using System.Collections.Generic;
using System.Globalization;

namespace FlappyReDovahLauncher
{
    /// <summary>RU/EN UI strings. Default: OS Russian → ru, otherwise en.</summary>
    internal static class Loc
    {
        public const string Ru = "ru";
        public const string En = "en";

        public static event Action LanguageChanged;

        public static string Language { get; private set; }

        public static bool IsRu { get { return Language == Ru; } }

        public static void Initialize(string saved)
        {
            string lang = Normalize(saved);
            if (string.IsNullOrEmpty(lang))
            {
                try
                {
                    string os = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                    lang = string.Equals(os, "ru", StringComparison.OrdinalIgnoreCase) ? Ru : En;
                }
                catch { lang = En; }
            }
            Language = lang;
        }

        public static void SetLanguage(string lang)
        {
            lang = Normalize(lang);
            if (string.IsNullOrEmpty(lang)) lang = En;
            if (lang == Language) return;
            Language = lang;
            var h = LanguageChanged;
            if (h != null) h();
        }

        public static string Normalize(string lang)
        {
            if (string.IsNullOrWhiteSpace(lang)) return "";
            lang = lang.Trim().ToLowerInvariant();
            if (lang.StartsWith("ru")) return Ru;
            if (lang.StartsWith("en")) return En;
            return "";
        }

        public static string T(string key)
        {
            Dictionary<string, string> map = IsRu ? RuMap : EnMap;
            string s;
            if (map.TryGetValue(key, out s) && !string.IsNullOrEmpty(s))
                return s;
            if (EnMap.TryGetValue(key, out s) && !string.IsNullOrEmpty(s))
                return s;
            return key;
        }

        public static string F(string key, params object[] args)
        {
            try { return string.Format(T(key), args); }
            catch { return T(key); }
        }

        public static string GameBlurb(GameDefinition g)
        {
            if (g == null) return "";
            if (!g.Available) return F("coming_soon_named", g.Title);
            if (string.Equals(g.Id, "re-dovah", StringComparison.OrdinalIgnoreCase))
                return T("desc_redovah");
            if (string.Equals(g.Id, "flappy", StringComparison.OrdinalIgnoreCase))
                return T("desc_flappy");
            return g.Description ?? g.Title;
        }

        private static readonly Dictionary<string, string> EnMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "settings", "Settings" },
            { "language", "Language" },
            { "downloads", "Parallel downloads" },
            { "close", "Close" },
            { "play", "Play" },
            { "install", "Install" },
            { "update", "Update" },
            { "soon", "Soon" },
            { "modding", "Modding" },
            { "repair", "Repair" },
            { "cancel", "Cancel" },
            { "get_vr", "Install VR" },
            { "remove_vr", "Remove VR" },
            { "bugreport", "Bug report" },
            { "uninstall", "Uninstall game" },
            { "ready", "Ready to play" },
            { "checking", "Checking…" },
            { "update_available", "Update available" },
            { "ready_install", "Ready to install" },
            { "coming_soon", "Coming soon" },
            { "coming_soon_named", "{0} is not packaged yet.\n\nComing soon." },
            { "desc_redovah", "Skyrim AE + VR modpack" },
            { "desc_flappy", "Coming soon — Flappy 4.0.0" },
            { "cancelling", "Cancelling…\nPlease wait" },
            { "scan_fp", "Scanning packages…\nFingerprints" },
            { "repair_not_needed", "Repair not needed\nAll packages match" },
            { "repairing", "Repairing…\n{0} packages" },
            { "get_vr_status", "Install VR…\nDownloading VR packages" },
            { "remove_vr_status", "Removing VR…\nPlease wait" },
            { "preparing_ae", "Preparing…\nAE only" },
            { "preparing_vr", "Preparing…\nAE + VR" },
            { "ver_game_dash", "Game —  ·  Launcher {0}" },
            { "ver_not_installed", "Game {0} (not installed)  ·  Launcher {1}" },
            { "ver_game_dash_only", "Game —  ·  Launcher {0}" },
            { "ver_upgrade", "Game {0} → {1}  ·  Launcher {2}" },
            { "ver_ok", "Game {0}  ·  Launcher {1}" },
            { "updating", "Updating…" },
            { "channel_title", "What to download?" },
            { "channel_hint", "VR packs are large (StockGameVR + (VR) mods).\nYou can add VR later from Settings." },
            { "channel_ae", "AE only\nSkyrim AE — no VR" },
            { "channel_both", "AE + VR\nFull pack" },
            { "mode_title", "Choose mode" },
            { "mode_last", "Last choice: {0}" },
            { "mode_none", "none" },
            { "mode_ae", "AE\nSkyrim AE" },
            { "mode_vr", "VR\nSkyrim VR" },
            { "mo", "Mod Organizer" },
            { "launch_failed", "Launch failed" },
            { "link_failed", "Cannot open link:\n{0}" },
            { "job_install", "Install" },
            { "job_update", "Update" },
            { "job_repair", "Repair" },
            { "job_get_vr", "Install VR" },
            { "job_remove_vr", "Remove VR" },
            { "fail_install", "Install / Update failed" },
            { "fail_repair", "Repair failed" },
            { "fail_get_vr", "Install VR failed" },
            { "fail_remove_vr", "Remove VR failed" },
            { "fail_bugreport", "Bug report failed" },
            { "fail_uninstall", "Uninstall failed" },
            { "repair_ask", "Repair compares local folders to CDN fingerprints (path|size).\n\n• Unchanged packages are skipped\n• profiles (MO2 user data) are kept if present\n• Incomplete/changed packages are re-downloaded\n• A report is written next to the launcher\n\nScan now?" },
            { "repair_ok", "All packages match the CDN fingerprints.\nNothing to download.\n\nSee repair_report_{0}.txt" },
            { "repair_to_dl", "Packages to re-download: {0}" },
            { "repair_more", "• … and {0} more" },
            { "repair_approx", "Approx. download: {0}" },
            { "repair_details", "Details: repair_report_{0}.txt" },
            { "repair_go", "Download and repair these packages?" },
            { "get_vr_ask", "Download VR packages (StockGameVR, (VR) mods, VR profile)?\n\nThis can take a long time and needs extra disk space.\nAE content already installed will be kept." },
            { "remove_vr_ask", "Remove all VR packages from this install?\n\nDeletes StockGameVR, (VR) mods, and the VR profile.\nAE content stays. You can install VR later from Settings." },
            { "uninstall_ask", "Delete the local install of {0}?\n\nThis removes:\n{1}\n\nThe launcher itself is kept. You can install the game again later." },
            { "uninstall_busy", "Wait until the current download/repair finishes." },
            { "settings_hint", "{0}\nChannel: {1}" },
            { "channel_ae_short", "AE only" },
            { "channel_full_short", "AE + VR" },
            { "channel_none", "not installed" },
            { "settings_game_tools", "This game" },
            { "need_install", "Install the game first." },
            { "vr_unsupported", "This title has no VR pack." },
            { "tray_open", "Open" },
            { "tray_exit", "Exit" },
            { "tray_tip", "Flappy Launcher — double-click to open" },
            { "bugreport_wait", "Collecting bug report…\nPlease wait" },
            { "bugreport_done_title", "Bug report" },
            { "bugreport_done", "Archive created:\n{0}\n\nSend this file to the Discord support chat." },
            { "bugreport_open_folder", "The folder with the archive will open now." },
            { "self_fail", "Launcher update failed.\n\n{0}\n\nYou can keep using this version, or download the latest zip from:\n{1}\nand replace {2} (game folders stay)." },
            { "self_fail_title", "{0} — Update failed" },
            { "self_ask", "A new {0} version is available.\n\nInstalled:  {1}\nAvailable:  {2}{3}\n\nUpdate now?\n(Game install folders will not be deleted.)" },
            { "self_ask_title", "{0} — Update" },
            { "self_req", "A required {0} update will be installed.\n\nInstalled:  {1}\nAvailable:  {2}{3}" },
            { "self_req_title", "{0} — Required update" },
            { "ae_only_vr", "This install is AE-only.\n\nUse Settings → Install VR to download VR packages." },
            { "checking_fp", "Checking fingerprints…\nPlease wait" },
            { "checking_n", "Checking…\n{0}" },
            { "checking_vr", "Checking VR packages…" },
            { "dl_start", "Downloading {0} package(s)\n{1} workers · starting…" },
            { "dl_local", "Packages: {0}\nLocal prefer + CDN fallback · starting…" },
            { "extracting", "Extracting {0}/{1}:\n{2}" },
            { "job_complete_ok", "{0} complete.\nAll packages OK ({1})" },
            { "job_complete", "{0} complete.\n{1} package(s)" },
            { "no_vr_index", "No VR packages in index." },
            { "vr_already", "VR packages already present.\nChannel: AE + VR" },
            { "vr_removed", "VR removed.\nChannel: AE only" },
            { "removing_vr", "Removing VR:\n{0}" },
            { "sevenzip_missing", "7-Zip is not available.\n\nThe launcher downloads it from https://www.7-zip.org/ on first install.\nCheck your network, or install 7-Zip system-wide." },
            { "mo_missing", "ModOrganizer.exe not found.\nInstall or Repair the pack first." },
        };

        private static readonly Dictionary<string, string> RuMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "settings", "Настройки" },
            { "language", "Язык" },
            { "downloads", "Параллельные загрузки" },
            { "close", "Закрыть" },
            { "play", "Играть" },
            { "install", "Установить" },
            { "update", "Обновить" },
            { "soon", "Скоро" },
            { "modding", "Моды" },
            { "repair", "Починка" },
            { "cancel", "Отмена" },
            { "get_vr", "Установить VR" },
            { "remove_vr", "Удалить VR" },
            { "bugreport", "Багрепорт" },
            { "uninstall", "Удалить игру" },
            { "ready", "Можно играть" },
            { "checking", "Проверка…" },
            { "update_available", "Доступно обновление" },
            { "ready_install", "Готово к установке" },
            { "coming_soon", "Скоро" },
            { "coming_soon_named", "{0} ещё не выложен.\n\nСкоро." },
            { "desc_redovah", "Сборка Skyrim AE + VR" },
            { "desc_flappy", "Скоро — Flappy 4.0.0" },
            { "cancelling", "Отмена…\nПодождите" },
            { "scan_fp", "Сканирование…\nОтпечатки пакетов" },
            { "repair_not_needed", "Починка не нужна\nВсё совпадает с CDN" },
            { "repairing", "Починка…\n{0} пакетов" },
            { "get_vr_status", "Установка VR…\nЗагрузка пакетов" },
            { "remove_vr_status", "Удаление VR…\nПодождите" },
            { "preparing_ae", "Подготовка…\nТолько AE" },
            { "preparing_vr", "Подготовка…\nAE + VR" },
            { "ver_game_dash", "Игра —  ·  Лаунчер {0}" },
            { "ver_not_installed", "Игра {0} (не установлена)  ·  Лаунчер {1}" },
            { "ver_game_dash_only", "Игра —  ·  Лаунчер {0}" },
            { "ver_upgrade", "Игра {0} → {1}  ·  Лаунчер {2}" },
            { "ver_ok", "Игра {0}  ·  Лаунчер {1}" },
            { "updating", "Обновление…" },
            { "channel_title", "Что скачать?" },
            { "channel_hint", "VR-пакеты большие (StockGameVR и моды (VR)).\nVR можно добавить позже в Настройках." },
            { "channel_ae", "Только AE\nSkyrim AE — без VR" },
            { "channel_both", "AE + VR\nПолная сборка" },
            { "mode_title", "Режим запуска" },
            { "mode_last", "Последний выбор: {0}" },
            { "mode_none", "нет" },
            { "mode_ae", "AE\nSkyrim AE" },
            { "mode_vr", "VR\nSkyrim VR" },
            { "mo", "Mod Organizer" },
            { "launch_failed", "Не удалось запустить" },
            { "link_failed", "Не открывается ссылка:\n{0}" },
            { "job_install", "Установка" },
            { "job_update", "Обновление" },
            { "job_repair", "Починка" },
            { "job_get_vr", "Установка VR" },
            { "job_remove_vr", "Удаление VR" },
            { "fail_install", "Ошибка установки / обновления" },
            { "fail_repair", "Ошибка починки" },
            { "fail_get_vr", "Не удалось установить VR" },
            { "fail_remove_vr", "Не удалось удалить VR" },
            { "fail_bugreport", "Ошибка багрепорта" },
            { "fail_uninstall", "Не удалось удалить игру" },
            { "repair_ask", "Починка сравнивает папки с отпечатками CDN (путь|размер).\n\n• Неизменённые пакеты пропускаются\n• profiles (данные MO2) сохраняются, если есть\n• Битые/изменённые пакеты скачиваются заново\n• Отчёт пишется рядом с лаунчером\n\nСканировать сейчас?" },
            { "repair_ok", "Все пакеты совпадают с CDN.\nКачать нечего.\n\nСм. repair_report_{0}.txt" },
            { "repair_to_dl", "Пакеты на перекачку: {0}" },
            { "repair_more", "• … и ещё {0}" },
            { "repair_approx", "Примерно скачать: {0}" },
            { "repair_details", "Подробности: repair_report_{0}.txt" },
            { "repair_go", "Скачать и починить эти пакеты?" },
            { "get_vr_ask", "Скачать VR-пакеты (StockGameVR, моды (VR), профиль VR)?\n\nЭто долго и занимает место на диске.\nУже установленный AE не удаляется." },
            { "remove_vr_ask", "Удалить все VR-пакеты из этой установки?\n\nСтираются StockGameVR, моды (VR) и профиль VR.\nAE остаётся. VR можно поставить снова в Настройках." },
            { "uninstall_ask", "Удалить локальную установку {0}?\n\nБудет стёрта папка:\n{1}\n\nСам лаунчер останется. Игру можно установить снова." },
            { "uninstall_busy", "Дождитесь окончания загрузки или починки." },
            { "settings_hint", "{0}\nКанал: {1}" },
            { "channel_ae_short", "только AE" },
            { "channel_full_short", "AE + VR" },
            { "channel_none", "не установлена" },
            { "settings_game_tools", "Эта игра" },
            { "need_install", "Сначала установите игру." },
            { "vr_unsupported", "У этой игры нет VR-пакета." },
            { "tray_open", "Открыть" },
            { "tray_exit", "Выход" },
            { "tray_tip", "Flappy Launcher — двойной щелчок, чтобы открыть" },
            { "bugreport_wait", "Сбор багрепорта…\nПодождите" },
            { "bugreport_done_title", "Багрепорт" },
            { "bugreport_done", "Архив создан:\n{0}\n\nОтправьте этот файл в чат поддержки Discord." },
            { "bugreport_open_folder", "Сейчас откроется папка с архивом." },
            { "self_fail", "Не удалось обновить лаунчер.\n\n{0}\n\nМожно продолжить с этой версией или скачать zip:\n{1}\nи заменить {2} (папки игр не трогать)." },
            { "self_fail_title", "{0} — ошибка обновления" },
            { "self_ask", "Доступна новая версия {0}.\n\nСейчас:     {1}\nДоступно:  {2}{3}\n\nОбновить?\n(Папки игр не удаляются.)" },
            { "self_ask_title", "{0} — обновление" },
            { "self_req", "Будет установлено обязательное обновление {0}.\n\nСейчас:     {1}\nДоступно:  {2}{3}" },
            { "self_req_title", "{0} — обязательное обновление" },
            { "ae_only_vr", "Сейчас стоит только AE.\n\nVR качается через Настройки → Установить VR." },
            { "checking_fp", "Проверка отпечатков…\nПодождите" },
            { "checking_n", "Проверка…\n{0}" },
            { "checking_vr", "Проверка VR-пакетов…" },
            { "dl_start", "Загрузка {0} пак.\n{1} потока · старт…" },
            { "dl_local", "Пакетов: {0}\nСначала локально, иначе CDN · старт…" },
            { "extracting", "Распаковка {0}/{1}:\n{2}" },
            { "job_complete_ok", "{0} готово.\nВсе пакеты в порядке ({1})" },
            { "job_complete", "{0} готово.\n{1} пак." },
            { "no_vr_index", "В index.json нет VR-пакетов." },
            { "vr_already", "VR уже на месте.\nКанал: AE + VR" },
            { "vr_removed", "VR удалён.\nКанал: только AE" },
            { "removing_vr", "Удаление VR:\n{0}" },
            { "sevenzip_missing", "7-Zip недоступен.\n\nЛаунчер качает его с https://www.7-zip.org/ при первой установке.\nПроверьте сеть или поставьте 7-Zip в систему." },
            { "mo_missing", "Не найден ModOrganizer.exe.\nСначала Установить или Починка." },
        };
    }
}
