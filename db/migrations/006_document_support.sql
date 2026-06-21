-- 006_document_support.sql
-- Extend capture type constraints to support 'document' (e.g. PDF) messages,
-- including downloadable document media artifacts.

alter table inbound_message_event drop constraint if exists inbound_message_event_payload_type_check;
alter table inbound_message_event
    add constraint inbound_message_event_payload_type_check
    check (payload_type in ('text', 'image', 'audio', 'document', 'forwarded', 'unsupported'));

alter table unified_message_artifact drop constraint if exists unified_message_artifact_message_kind_check;
alter table unified_message_artifact
    add constraint unified_message_artifact_message_kind_check
    check (message_kind in ('text', 'image', 'audio', 'document', 'forwarded', 'unsupported'));

alter table media_artifact drop constraint if exists media_artifact_media_type_check;
alter table media_artifact
    add constraint media_artifact_media_type_check
    check (media_type in ('image', 'audio', 'document'));
