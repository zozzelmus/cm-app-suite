namespace Conduct.Domain.Authorization;

// Single source of truth for permission keys. Roles are runtime-configurable bundles of these.
// Authorization checks reference these constants — never the literal string elsewhere in code.
public static class Permissions
{
    // Case lifecycle
    public const string CaseRead              = "case.read";
    public const string CaseCreate            = "case.create";
    public const string CaseUpdate            = "case.update";
    public const string CaseClose             = "case.close";
    public const string CaseReopen            = "case.reopen";              // Closed → Investigating; LifecycleJson references this

    // Transfers + tasks
    public const string CaseTransferInitiate  = "case.transfer.initiate";
    public const string CaseTransferApprove   = "case.transfer.approve";   // generic — granted by virtue of being approver
    public const string TaskApproveLobManager = "task.approve.lob_manager"; // specifically: act as LOB manager on approval tasks

    // Notes
    public const string NoteWrite             = "casenote.write";
    public const string NoteViewPrivileged    = "note.view.privileged";    // see notes w/ Visibility=Privileged

    // Evidence
    public const string EvidenceUpload        = "evidence.upload";
    public const string EvidenceView          = "evidence.view";
    public const string EvidenceViewPii       = "evidence.view.pii";
    public const string EvidenceViewPrivileged= "evidence.view.privileged";
    public const string EvidenceViewRestricted= "evidence.view.restricted";
    public const string EvidenceClassify      = "evidence.classify";       // promote sensitivity tier

    // LOB / CaseType admin
    public const string LobAdmin              = "lob.admin";
    public const string LobMembershipManage   = "lob.membership.manage";
    public const string CaseTypeAdmin         = "casetype.admin";
    public const string RoleAdmin             = "role.admin";              // create/edit Role entities
    public const string AssignmentManage      = "assignment.manage";

    // Audit + system
    public const string AuditView             = "audit.view";
    public const string AuditExport           = "audit.export";
    public const string SystemAdmin           = "system.admin";            // bypass everything; logs more aggressively
}
