using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using ScheduleReminder.Domain;

namespace ScheduleReminder.Services
{
    [DataContract]
    public sealed class AiCommandSet
    {
        [DataMember(Name = "commands")]
        public List<AiCommand> Commands { get; set; }
    }

    [DataContract]
    public sealed class AiCommand
    {
        [DataMember(Name = "seq")]
        public int Seq { get; set; }

        [DataMember(Name = "type")]
        public string Type { get; set; }

        [DataMember(Name = "op")]
        public string Op { get; set; }

        [DataMember(Name = "targetId", EmitDefaultValue = false)]
        public string TargetId { get; set; }

        [DataMember(Name = "title", EmitDefaultValue = false)]
        public string Title { get; set; }

        [DataMember(Name = "name", EmitDefaultValue = false)]
        public string Name { get; set; }

        [DataMember(Name = "date", EmitDefaultValue = false)]
        public string Date { get; set; }

        [DataMember(Name = "time", EmitDefaultValue = false)]
        public string Time { get; set; }

        [DataMember(Name = "advanceMinutes", EmitDefaultValue = false)]
        public int? AdvanceMinutes { get; set; }

        [DataMember(Name = "note", EmitDefaultValue = false)]
        public string Note { get; set; }

        [DataMember(Name = "remindEnabled", EmitDefaultValue = false)]
        public bool? RemindEnabled { get; set; }

        [DataMember(Name = "isTodo", EmitDefaultValue = false)]
        public bool? IsTodo { get; set; }

        [DataMember(Name = "tagId", EmitDefaultValue = false)]
        public string TagId { get; set; }

        [DataMember(Name = "bgColor", EmitDefaultValue = false)]
        public string BgColor { get; set; }

        [DataMember(Name = "fontColor", EmitDefaultValue = false)]
        public string FontColor { get; set; }

        [DataMember(Name = "sound", EmitDefaultValue = false)]
        public object Sound { get; set; }

        [DataMember(Name = "recurrence", EmitDefaultValue = false)]
        public RecurrenceRule Recurrence { get; set; }

        [DataMember(Name = "occurrenceDate", EmitDefaultValue = false)]
        public string OccurrenceDate { get; set; }

        [DataMember(Name = "done", EmitDefaultValue = false)]
        public bool? Done { get; set; }
    }

    [DataContract]
    public sealed class AiCommandResult
    {
        [DataMember(Name = "seq")]
        public int Seq { get; set; }

        [DataMember(Name = "ok")]
        public bool Ok { get; set; }

        [DataMember(Name = "newId", EmitDefaultValue = false)]
        public string NewId { get; set; }

        [DataMember(Name = "reason", EmitDefaultValue = false)]
        public string Reason { get; set; }
    }

    [DataContract]
    public sealed class AiSnapshot
    {
        [DataMember(Name = "generatedAt")]
        public string GeneratedAt { get; set; }

        [DataMember(Name = "schemaVersion")]
        public int SchemaVersion { get; set; }

        [DataMember(Name = "notice")]
        public string Notice { get; set; }

        [DataMember(Name = "tags")]
        public List<TagTemplate> Tags { get; set; }

        [DataMember(Name = "schedules")]
        public List<Schedule> Schedules { get; set; }
    }
}
