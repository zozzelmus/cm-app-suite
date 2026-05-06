using Conduct.Domain.Common;

namespace Conduct.Domain.Cases;

// Investigative journal entry. Markdown content. Editable while parent Case is not in a frozen state
// (default: Closed locks notes; per-CaseType override possible). Every edit captured in audit log.
public class CaseNote : TenantedEntity
{
    public Guid CaseId { get; set; }
    public Guid AuthorUserId { get; set; }
    public string Content { get; set; } = string.Empty;               // markdown
    public NoteVisibility Visibility { get; set; } = NoteVisibility.Internal;
    public Guid? ParentNoteId { get; set; }                           // threading (optional MVP)
    public DateTimeOffset? LastEditedAt { get; set; }
    public bool IsDeletedFlag { get; set; }                           // soft-delete; original content preserved in audit
}

public enum NoteVisibility
{
    Internal = 0,                                                     // visible to anyone with case visibility
    Privileged = 1,                                                   // requires note.view.privileged (Legal LOB typical)
    ManagerOnly = 2,                                                  // requires task.approve.lob_manager on owner LOB
}
