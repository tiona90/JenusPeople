import { describe, expect, it } from 'vitest'
import {
    approvalRule, approveButtonLabel, approveOutcome, autoApproves, canApproveInDialog, canCancelApproved, canDecide,
    canRejectInDialog, isManagersRejection, isOpenStatus, isTimesheetWithManager, isWithManager, statusChipLabel, statusPhrase,
} from './approval-stage'

/**
 * Mirror of `Application/AnnualLeaves/Commands/ApprovalStageRule.cs` and
 * `CancellationRule.cs`. The server decides the stage; this decides which buttons a
 * page offers, so it must never offer one the server refuses — and may under-offer
 * when the type is not loaded.
 */
const MANAGER = { isHrAdministrator: false }
const HR = { isHrAdministrator: true }
const both = { requiresManagerApproval: true, requiresHrApproval: true }
const managerOnly = { requiresManagerApproval: true, requiresHrApproval: false }
const hrOnly = { requiresManagerApproval: false, requiresHrApproval: true }
const neither = { requiresManagerApproval: false, requiresHrApproval: false }

describe('isOpenStatus', () => {
    it('is Pending or AwaitingHrApproval', () => {
        expect(isOpenStatus('Pending')).toBe(true)
        expect(isOpenStatus('AwaitingHrApproval')).toBe(true)
        expect(isOpenStatus('Approved')).toBe(false)
        expect(isOpenStatus('Rejected')).toBe(false)
        expect(isOpenStatus('Cancelled')).toBe(false)
    })
})

describe('isWithManager', () => {
    it("is a Pending request on a type asking for the manager, seen by HR", () => {
        expect(isWithManager({ status: 'Pending' }, managerOnly, HR)).toBe(true)
        expect(isWithManager({ status: 'Pending' }, both, HR)).toBe(true)
    })
    it('reads a type not yet loaded as manager-only, like the server reads a deleted one', () => {
        expect(isWithManager({ status: 'Pending' }, undefined, HR)).toBe(true)
    })
    it('is never the case for a manager, a type with no manager stage, or a row past Pending', () => {
        expect(isWithManager({ status: 'Pending' }, managerOnly, MANAGER)).toBe(false)
        expect(isWithManager({ status: 'Pending' }, hrOnly, HR)).toBe(false)
        expect(isWithManager({ status: 'AwaitingHrApproval' }, both, HR)).toBe(false)
        expect(isWithManager({ status: 'Approved' }, managerOnly, HR)).toBe(false)
    })
})

describe('isManagersRejection', () => {
    it("is a rejection the manager made, seen by HR", () => {
        expect(isManagersRejection({ status: 'Rejected' }, managerOnly, HR, 'Pending')).toBe(true)
        expect(isManagersRejection({ status: 'Rejected' }, both, HR, 'Pending')).toBe(true)
        // An approval the manager took back is the manager's too.
        expect(isManagersRejection({ status: 'Rejected' }, managerOnly, HR, 'Approved')).toBe(true)
    })
    it("is not HR's own rejection from the HR stage", () => {
        expect(isManagersRejection({ status: 'Rejected' }, both, HR, 'AwaitingHrApproval')).toBe(false)
        expect(isManagersRejection({ status: 'Rejected' }, hrOnly, HR, 'AwaitingHrApproval')).toBe(false)
    })
    it('falls back to the type when no history says where the rejection came from', () => {
        expect(isManagersRejection({ status: 'Rejected' }, managerOnly, HR, null)).toBe(true)
        expect(isManagersRejection({ status: 'Rejected' }, undefined, HR, undefined)).toBe(true)
        expect(isManagersRejection({ status: 'Rejected' }, hrOnly, HR, null)).toBe(false)
    })
    it('is never the case for a manager or for a row that is not rejected', () => {
        expect(isManagersRejection({ status: 'Rejected' }, managerOnly, MANAGER, 'Pending')).toBe(false)
        expect(isManagersRejection({ status: 'Approved' }, managerOnly, HR, 'Pending')).toBe(false)
        expect(isManagersRejection({ status: 'Cancelled' }, managerOnly, HR, 'Approved')).toBe(false)
    })
})

describe('isTimesheetWithManager', () => {
    it("is a submitted timesheet the server says a manager is available for, seen by HR", () => {
        expect(isTimesheetWithManager({ status: 'Submitted', awaitingManager: true }, HR)).toBe(true)
        expect(isTimesheetWithManager({ status: 'Resubmitted', awaitingManager: true }, HR)).toBe(true)
    })
    it('is not one nobody but HR can review', () => {
        expect(isTimesheetWithManager({ status: 'Submitted', awaitingManager: false }, HR)).toBe(false)
    })
    it('reads a missing flag (an older API) as not with the manager, so the server answers', () => {
        expect(isTimesheetWithManager({ status: 'Submitted' }, HR)).toBe(false)
    })
    it('is never the case for a manager or a decided sheet', () => {
        expect(isTimesheetWithManager({ status: 'Submitted', awaitingManager: true }, MANAGER)).toBe(false)
        expect(isTimesheetWithManager({ status: 'Approved', awaitingManager: true }, HR)).toBe(false)
    })
})

describe('canDecide', () => {
    it('lets a manager decide a Pending request', () => {
        expect(canDecide({ status: 'Pending' }, MANAGER, managerOnly)).toBe(true)
        expect(canDecide({ status: 'Pending' }, MANAGER, both)).toBe(true)
        expect(canDecide({ status: 'Pending' }, MANAGER, undefined)).toBe(true)
    })
    it('keeps HR off a Pending request that is with the manager', () => {
        expect(canDecide({ status: 'Pending' }, HR, managerOnly)).toBe(false)
        expect(canDecide({ status: 'Pending' }, HR, both)).toBe(false)
        expect(canDecide({ status: 'Pending' }, HR, undefined)).toBe(false)
    })
    it('lets HR decide a Pending request on a type with no manager stage', () => {
        expect(canDecide({ status: 'Pending' }, HR, hrOnly)).toBe(true)
    })
    it('lets only HR decide a request that is with HR', () => {
        expect(canDecide({ status: 'AwaitingHrApproval' }, MANAGER, both)).toBe(false)
        expect(canDecide({ status: 'AwaitingHrApproval' }, HR, both)).toBe(true)
    })
    it('offers nothing on a decided request', () => {
        expect(canDecide({ status: 'Approved' }, HR, both)).toBe(false)
        expect(canDecide({ status: 'Rejected' }, HR, both)).toBe(false)
    })
})

describe('canCancelApproved', () => {
    const today = new Date(2026, 8, 24) // 24 September 2026, local
    it('offers Cancel on an approved request starting today or later', () => {
        expect(canCancelApproved({ status: 'Approved', startDate: '2026-09-24T00:00:00' }, today)).toBe(true)
        expect(canCancelApproved({ status: 'Approved', startDate: '2026-10-01T00:00:00' }, today)).toBe(true)
    })
    it('offers none once the leave has started', () => {
        expect(canCancelApproved({ status: 'Approved', startDate: '2026-09-23T00:00:00' }, today)).toBe(false)
    })
    it('offers none on a request that is not approved', () => {
        expect(canCancelApproved({ status: 'Pending', startDate: '2026-10-01T00:00:00' }, today)).toBe(false)
        expect(canCancelApproved({ status: 'Cancelled', startDate: '2026-10-01T00:00:00' }, today)).toBe(false)
    })
    it('ignores the time of day on either side', () => {
        expect(canCancelApproved({ status: 'Approved', startDate: '2026-09-24T09:30:00' }, new Date(2026, 8, 24, 17, 45))).toBe(true)
    })
})

describe('approveOutcome', () => {
    it("sends a manager's approve to HR when the type asks for HR", () => {
        expect(approveOutcome({ status: 'Pending' }, both, MANAGER)).toBe('awaiting-hr')
    })
    it('finishes for HR from the HR stage', () => {
        expect(approveOutcome({ status: 'AwaitingHrApproval' }, both, HR)).toBe('approved')
    })
    it('finishes for a manager on a manager-only type, or when the type is not loaded', () => {
        expect(approveOutcome({ status: 'Pending' }, managerOnly, MANAGER)).toBe('approved')
        expect(approveOutcome({ status: 'Pending' }, undefined, MANAGER)).toBe('approved')
    })
    it('sends a reopened Rejected request to HR the same way a Pending one would', () => {
        expect(approveOutcome({ status: 'Rejected' }, both, MANAGER)).toBe('awaiting-hr')
        expect(approveOutcome({ status: 'Rejected' }, both, HR)).toBe('approved')
    })
    it('labels the button accordingly', () => {
        expect(approveButtonLabel('awaiting-hr')).toBe('Approve & send to HR')
        expect(approveButtonLabel('approved')).toBe('Approve')
    })
})

describe('canApproveInDialog', () => {
    it('offers Approve on a rejected request being reopened, either role', () => {
        expect(canApproveInDialog({ status: 'Rejected' }, MANAGER, both)).toBe(true)
        expect(canApproveInDialog({ status: 'Rejected' }, HR, both)).toBe(true)
    })
    it('offers Approve on a Pending request to the manager, not to HR', () => {
        expect(canApproveInDialog({ status: 'Pending' }, MANAGER, both)).toBe(true)
        expect(canApproveInDialog({ status: 'Pending' }, HR, both)).toBe(false)
    })
    it('offers Approve on a request with HR only to HR', () => {
        expect(canApproveInDialog({ status: 'AwaitingHrApproval' }, MANAGER, both)).toBe(false)
        expect(canApproveInDialog({ status: 'AwaitingHrApproval' }, HR, both)).toBe(true)
    })
    it('offers no Approve on an already-approved or cancelled request', () => {
        expect(canApproveInDialog({ status: 'Approved' }, HR, both)).toBe(false)
        expect(canApproveInDialog({ status: 'Cancelled' }, HR, both)).toBe(false)
    })
})

describe('canRejectInDialog', () => {
    it('offers Reject on an approval being taken back, either role', () => {
        expect(canRejectInDialog({ status: 'Approved' }, MANAGER, both)).toBe(true)
        expect(canRejectInDialog({ status: 'Approved' }, HR, both)).toBe(true)
    })
    it('offers Reject on a Pending request to the manager, not to HR', () => {
        expect(canRejectInDialog({ status: 'Pending' }, MANAGER, both)).toBe(true)
        expect(canRejectInDialog({ status: 'Pending' }, HR, both)).toBe(false)
    })
    it('offers Reject on a request with HR only to HR', () => {
        expect(canRejectInDialog({ status: 'AwaitingHrApproval' }, MANAGER, both)).toBe(false)
        expect(canRejectInDialog({ status: 'AwaitingHrApproval' }, HR, both)).toBe(true)
    })
    it('offers no Reject on an already-rejected or cancelled request', () => {
        expect(canRejectInDialog({ status: 'Rejected' }, HR, both)).toBe(false)
        expect(canRejectInDialog({ status: 'Cancelled' }, HR, both)).toBe(false)
    })
})

describe('the type-level readings', () => {
    it('autoApproves only when both switches are off', () => {
        expect(autoApproves(neither)).toBe(true)
        expect(autoApproves(managerOnly)).toBe(false)
        expect(autoApproves(hrOnly)).toBe(false)
        expect(autoApproves(undefined)).toBe(false)
    })
    it('names the rule for the card', () => {
        expect(approvalRule(neither)).toBe('auto')
        expect(approvalRule(managerOnly)).toBe('manager')
        expect(approvalRule(hrOnly)).toBe('hr')
        expect(approvalRule(both)).toBe('manager-then-hr')
    })
    it('reads a missing HR flag (an older API) as off', () => {
        expect(approvalRule({ requiresManagerApproval: true })).toBe('manager')
    })
})

describe('statusChipLabel', () => {
    it('spells the HR stage out', () => {
        expect(statusChipLabel('AwaitingHrApproval')).toBe('Awaiting HR approval')
        expect(statusChipLabel('Pending')).toBe('Pending')
    })
})

describe('statusPhrase', () => {
    it('reads the HR stage as a sentence fragment', () => {
        expect(statusPhrase('AwaitingHrApproval')).toBe('awaiting HR approval')
    })
    it('falls back to the lower-cased status for everything else', () => {
        expect(statusPhrase('Approved')).toBe('approved')
        expect(statusPhrase('Pending')).toBe('pending')
        expect(statusPhrase('Rejected')).toBe('rejected')
        expect(statusPhrase('Cancelled')).toBe('cancelled')
    })
})

describe("nobody decides their own leave (UpdateLeaveStatus.OwnLeaveMessage)", () => {
    const self = 'u-me'
    const MANAGER_ME = { isHrAdministrator: false, userId: self }
    const HR_ME = { isHrAdministrator: true, userId: self }

    it('offers no Approve or Reject on the viewer\'s own open request', () => {
        expect(canDecide({ status: 'Pending', employeeId: self }, MANAGER_ME, managerOnly)).toBe(false)
        expect(canDecide({ status: 'AwaitingHrApproval', employeeId: self }, HR_ME, hrOnly)).toBe(false)
        expect(canApproveInDialog({ status: 'Rejected', employeeId: self }, MANAGER_ME, managerOnly)).toBe(false)
        expect(canRejectInDialog({ status: 'Approved', employeeId: self }, MANAGER_ME, managerOnly)).toBe(false)
    })

    it('still offers them on somebody else\'s request', () => {
        expect(canDecide({ status: 'Pending', employeeId: 'u-other' }, MANAGER_ME, managerOnly)).toBe(true)
        expect(canDecide({ status: 'AwaitingHrApproval', employeeId: 'u-other' }, HR_ME, hrOnly)).toBe(true)
        expect(canApproveInDialog({ status: 'Rejected', employeeId: 'u-other' }, MANAGER_ME, managerOnly)).toBe(true)
        expect(canRejectInDialog({ status: 'Approved', employeeId: 'u-other' }, MANAGER_ME, managerOnly)).toBe(true)
    })
})
