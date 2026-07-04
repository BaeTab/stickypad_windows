using System;
using System.Collections.Generic;
using StickyPad.Resources;

namespace StickyPad.Models;

/// 내장 노트 템플릿. Name 은 현재 언어로 지연 평가(리소스), Content 의 "{{date}}" 는 생성 시 오늘 날짜로 치환.
public sealed record NoteTemplate(Func<string> Name, string Content, NoteContentFormat Format, NoteColor Color);

public static class NoteTemplates
{
    public static IReadOnlyList<NoteTemplate> All { get; } = new List<NoteTemplate>
    {
        new(() => Strings.Template_DailyLog,
            "# {{date}}\n\n## To-do\n- [ ] \n\n## Notes\n", NoteContentFormat.Markdown, NoteColor.Blue),
        new(() => Strings.Template_Meeting,
            "# Meeting — {{date}}\n\nAttendees: \n\n## Agenda\n- \n\n## Decisions\n- \n\n## Action items\n- [ ] ",
            NoteContentFormat.Markdown, NoteColor.Green),
        new(() => Strings.Template_Todo,
            "# To-do\n\n- [ ] \n- [ ] \n- [ ] ", NoteContentFormat.Markdown, NoteColor.Yellow),
    };
}
