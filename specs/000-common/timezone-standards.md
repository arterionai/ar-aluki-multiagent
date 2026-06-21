# Timezone Standards

**Status**: Baseline  
**Version**: 1.0  
**Date**: 2026-06-21

## Overview

Timezone handling is critical for calendar integration (`003`), reminders (`005`), and billing cycles (`010`). This document standardizes representation, storage, and comparison across all features.

## Storage Requirements

### Rule 1: Store All User-Facing Datetimes as Tuples

**Format**: `(instant_utc, timezone_identifier)`

```csharp
record DatetimeWithTz(
    DateTime UtcInstant,  // Always UTC, e.g., "2026-06-21T14:32:10Z"
    string TimezoneId     // IANA identifier, e.g., "America/New_York"
);
```

### Rule 2: Never Use Numeric UTC Offsets for Storage

❌ **WRONG**:
```json
{
  "reminder_time": "2026-06-21T14:32:10-05:00"
}
```

✅ **CORRECT**:
```json
{
  "reminder_time_utc": "2026-06-21T19:32:10Z",
  "timezone_id": "America/New_York"
}
```

**Why**: Numeric offsets don't account for DST transitions and can cause ambiguity during clock changes.

### Rule 3: IANA Timezone Identifiers

Always use IANA identifiers from the [IANA Time Zone Database](https://www.iana.org/time-zones):

✅ **CORRECT**: `America/New_York`, `Europe/London`, `Asia/Tokyo`  
❌ **WRONG**: `EST`, `UTC-5`, `GMT+5:30`

## Database Schema

### PostgreSQL Implementation

```sql
-- For reminders, calendar events, billing cycles
CREATE TABLE events (
    id UUID PRIMARY KEY,
    tenant_id UUID NOT NULL,
    event_utc TIMESTAMP WITH TIME ZONE NOT NULL,
    timezone_id VARCHAR(50) NOT NULL,
    
    -- Indexes for common queries
    INDEX idx_tenant_event_utc (tenant_id, event_utc),
    
    CONSTRAINT valid_timezone CHECK (timezone_id IN (SELECT tzid FROM pg_tzdb))
);

-- For billing cycle timestamps
CREATE TABLE billing_cycles (
    id UUID PRIMARY KEY,
    tenant_id UUID NOT NULL,
    cycle_start_utc TIMESTAMP WITH TIME ZONE NOT NULL,
    cycle_end_utc TIMESTAMP WITH TIME ZONE NOT NULL,
    cycle_timezone_id VARCHAR(50) NOT NULL,
    
    CONSTRAINT valid_timezone CHECK (cycle_timezone_id IN (SELECT tzid FROM pg_tzdb))
);
```

## Conversions

### Rule 4: Convert for User Display

When returning datetimes to users, convert from UTC to their preferred timezone:

```csharp
public static DateTime ConvertUtcToUserTz(DateTime utcInstant, string timezoneId)
{
    var tzInfo = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
    return TimeZoneInfo.ConvertTimeFromUtc(utcInstant, tzInfo);
}

// Example: Store "2026-06-21T19:32:10Z" + "America/New_York"
// Display: "2026-06-21T14:32:10" (local time in NYC)
```

### Rule 5: Comparison & Scheduling

All comparisons in code must be UTC-based:

```csharp
// ✅ CORRECT: Always compare UTC instants
if (eventUtc < DateTime.UtcNow)
{
    // Event is in the past
}

// ❌ WRONG: Never compare local times from different timezones
if (eventLocal < userPreferredLocal) { }
```

## Feature-Specific Guidelines

### 003-Calendar Integration

- **Creation**: User specifies "June 21, 2026 at 2:30 PM" + timezone (e.g., "America/New_York")
  - Store as: `utc_instant = 2026-06-21T18:30:10Z`, `timezone_id = "America/New_York"`
  
- **Sync to External Calendar**: Convert back to user's timezone before sending to Google Calendar / Outlook API
  
- **DST Transitions**: When a user's timezone observes DST, automatically adjust reminder times (e.g., fall-back transitions)

### 005-Reminders

- **Creation**: User says "remind me at 2:30 PM tomorrow"
  - Store: Current user timezone inferred from profile (or session context)
  - Calculate UTC instant considering DST
  
- **Delivery**: Always compare against UTC `DateTime.UtcNow`
  
- **Rounding**: When user specifies "tomorrow at 2:30 PM", resolve "tomorrow" in their timezone before converting to UTC

Example workflow:
```
User input: "remind me at 2:30 PM tomorrow"
User timezone (from profile): "America/New_York"

Step 1: Resolve "tomorrow" in user's timezone → 2026-06-22
Step 2: Combine with time → 2026-06-22T14:30:00 (in America/New_York)
Step 3: Convert to UTC → 2026-06-22T18:30:00Z
Step 4: Store in DB: (utc=2026-06-22T18:30:00Z, timezone_id="America/New_York")

Delivery check:
- Query: SELECT * FROM reminders WHERE utc_instant <= UTC_NOW AND NOT delivered
- Deliver all matching reminders regardless of timezone
- Log delivery in UTC
```

### 010-Billing

- **Cycle Boundaries**: Billing cycles are anchored to UTC midnight
  - Example: Cycle = "2026-06-01T00:00:00Z" to "2026-06-30T23:59:59Z" (UTC)
  
- **Invoice Generation**: Always happens at fixed UTC time (e.g., end of month at 00:00:00 UTC)
  
- **Timezone Display**: Invoice PDF shows cycle dates in both UTC and tenant's preferred timezone (if applicable)

## Audit & Compliance

### Rule 6: Always Log Timezone Context

Every audit event involving a user-facing datetime MUST include:

```json
{
  "event_type": "reminder.created",
  "user_specified_time": "2026-06-22T14:30:00",
  "user_timezone_id": "America/New_York",
  "stored_utc_instant": "2026-06-22T18:30:00Z",
  "system_timezone_id": "UTC"
}
```

This ensures auditability and debuggability across DST transitions.

## Testing

### Rule 7: Test DST Boundaries

For any feature involving user-specified times:

1. **Test case**: Remind me at 2:00 AM during spring-forward transition (time does not exist)
2. **Test case**: Remind me at 1:30 AM during fall-back transition (time occurs twice)
3. **Expected**: Clear error or clarification prompt; never silently choose one interpretation

## External API Integration

### Google Calendar API

```csharp
var eventRequest = service.Events.Insert(new Event
{
    Summary = "My Event",
    Start = new EventDateTime
    {
        DateTime = userLocalTime,  // NOT UTC!
        TimeZone = timezoneId      // IANA identifier
    }
});
```

### Outlook / Microsoft Graph

```csharp
var event = new Event
{
    Subject = "My Event",
    Start = new DateTimeTimeZone
    {
        DateTime = userLocalTime,  // NOT UTC!
        TimeZone = timezoneId      // IANA identifier
    }
};
```

## Summary Table

| Context | Storage | Comparison | Display |
|---|---|---|---|
| **Reminder delivery** | `(utc_instant, timezone_id)` | Always UTC instant against `UtcNow` | User's timezone |
| **Calendar sync** | `(utc_instant, timezone_id)` | UTC for ordering | IANA TZ to external API |
| **Billing cycle** | UTC midnight anchors | UTC-based start/end | UTC in reports, local in UI |
| **Audit events** | Both UTC instant and user TZ context | Comparison in UTC | As logged |

---

## Implementation Checklist

- [ ] All feature data models document `timezone_id` storage location
- [ ] All queries use UTC instants for comparison
- [ ] All user-facing responses include timezone conversion
- [ ] Audit events include timezone context
- [ ] Unit tests cover DST transitions
- [ ] External API integrations pass IANA identifiers (not numeric offsets)
