using System;
using System.Collections.Generic;

internal static class Localize
{
    internal static readonly string[] Languages = { "uk", "ru", "en" };
    internal static readonly string[] Labels = { "УКР", "РУС", "ENG" };

    private static readonly Dictionary<string, string[]> Translations = new Dictionary<string, string[]>
    {
        { "Мова", new[] { "Язык", "Language" } },
        { "Налаштування", new[] { "Настройки", "Settings" } },
        { "Позиція", new[] { "Положение", "Position" } },
        { "Монітор", new[] { "Монитор", "Monitor" } },
        { "Скинути позицію", new[] { "Сбросить положение", "Reset position" } },
        { "Розмір і вигляд", new[] { "Размер и внешний вид", "Size and appearance" } },
        { "Масштаб", new[] { "Масштаб", "Scale" } },
        { "Масштаб, %", new[] { "Масштаб, %", "Scale, %" } },
        { "Відступ", new[] { "Интервал", "Spacing" } },
        { "Відступ, px", new[] { "Интервал, px", "Spacing, px" } },
        { "Кути", new[] { "Углы", "Corners" } },
        { "Кути, px", new[] { "Углы, px", "Corners, px" } },
        { "Непрозорість, %", new[] { "Непрозрачность, %", "Opacity, %" } },
        { "Кольори", new[] { "Цвета", "Colors" } },
        { "Фон", new[] { "Фон", "Background" } },
        { "Текст", new[] { "Текст", "Text" } },
        { "Іконки", new[] { "Значки", "Icons" } },
        { "Низький заряд", new[] { "Низкий заряд", "Low battery" } },
        { "Палітра…", new[] { "Палитра…", "Pick color…" } },
        { "Палітра", new[] { "Палитра", "Color picker" } },
        { "Запускати разом із Windows", new[] { "Запускать вместе с Windows", "Run with Windows" } },
        { "Прев’ю · перетягніть смужку", new[] { "Предпросмотр · перетащите полоску", "Preview · drag to move" } },
        { "Пристрої на смужці", new[] { "Устройства на полоске", "Devices on the strip" } },
        { "Вище", new[] { "Выше", "Move up" } },
        { "Нижче", new[] { "Ниже", "Move down" } },
        { "Ліміт", new[] { "Лимит", "Limit" } },
        { "Ліміт пристроїв", new[] { "Лимит устройств", "Device limit" } },
        { "Іконка + %", new[] { "Значок + %", "Icon + %" } },
        { "Тільки іконка", new[] { "Только значок", "Icon only" } },
        { "Тільки %", new[] { "Только %", "Percentage only" } },
        { "Зберегти", new[] { "Сохранить", "Save" } },
        { "Скасувати", new[] { "Отмена", "Cancel" } },
        { "Скинути до стандартних", new[] { "Сбросить настройки", "Reset to defaults" } },
        { "Формат #RRGGBB", new[] { "Формат #RRGGBB", "Use #RRGGBB" } },
        { "основний", new[] { "основной", "primary" } },
        { "Оновити", new[] { "Обновить", "Refresh" } },
        { "Вийти", new[] { "Выход", "Exit" } },
        { "Не вдалося змінити автозапуск Windows.", new[] { "Не удалось изменить автозапуск Windows.", "Could not change Windows startup." } },
        { "Не вдалося відновити попередній стан автозапуску.", new[] { "Не удалось восстановить прежнее состояние автозапуска.", "Could not restore the previous startup setting." } },
        { "Не вдалося зберегти налаштування.", new[] { "Не удалось сохранить настройки.", "Could not save settings." } },
        { "Немає доступних показників заряду", new[] { "Нет доступных показаний заряда", "No battery readings available" } },
        { "Зчитування показників…", new[] { "Чтение показаний…", "Reading battery levels…" } },
        { "Оновлено о ", new[] { "Обновлено в ", "Updated at " } },
        { "Заряд недоступний", new[] { "Заряд недоступен", "Battery level unavailable" } },
        { "Батарея ПК", new[] { "Батарея ПК", "PC battery" } },
        { "Заряджається", new[] { "Заряжается", "Charging" } },
        { "Підключено до живлення", new[] { "Подключено к питанию", "Plugged in" } },
        { "Працює від батареї", new[] { "Работает от батареи", "On battery power" } },
        { "Рівень від Windows", new[] { "Данные Windows", "Reported by Windows" } },
        { "Підключено · 2,4 ГГц", new[] { "Подключено · 2,4 ГГц", "Connected · 2.4 GHz" } },
        { "Заряджено", new[] { "Заряжено", "Fully charged" } },
        { "У базі", new[] { "В базе", "In base station" } },
        { "Arctis Nova Pro Wireless · гарнітура", new[] { "Arctis Nova Pro Wireless · гарнитура", "Arctis Nova Pro Wireless · headset" } },
        { "Arctis Nova Pro Wireless · батарея в базі", new[] { "Arctis Nova Pro Wireless · батарея в базе", "Arctis Nova Pro Wireless · spare battery" } }
    };

    internal static string Text(string language, string source)
    {
        int index = Array.IndexOf(Languages, language);
        string[] translated;
        return index > 0 && source != null && Translations.TryGetValue(source, out translated) ? translated[index - 1] : source;
    }
}
