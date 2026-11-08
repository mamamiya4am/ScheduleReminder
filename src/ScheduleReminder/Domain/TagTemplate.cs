using System;
using System.Runtime.Serialization;

namespace ScheduleReminder.Domain
{
    /// <summary>
    /// 标签 = 预设模板。一批「一键套用」的日程样式预设。
    ///
    /// 使用方式：在新建日程窗选中一个标签 → 把 Id / Name / BgColor / FontColor / Sound
    /// 拷贝进这条日程（即「快照」）。模板改名 / 改色 / 改铃声时，设置窗会按 TagId
    /// 把新值同步到所有引用它的日程；删除模板则只删模板本身，不清理日程上的快照。
    ///
    /// 空值约定（与 Schedule 现有语义保持一致，避免两套默认值）：
    ///   BgColor   空 = 透明
    ///   FontColor 空 = 跟随系统文字色
    ///   Sound     空 = 静音；不再有 default / custom / "-" 哨兵
    /// </summary>
    [DataContract]
    public class TagTemplate
    {
        /// <summary>稳定标识；格式由 IdGenerator.NewTagId() 生成。null = 旧数据未分配。</summary>
        [DataMember(Name = "id", EmitDefaultValue = false)]
        public string Id { get; set; }

        /// <summary>标签名（用户可见，最多 6 字；允许重名，判别一律按 Id）。</summary>
        [DataMember(Name = "name", EmitDefaultValue = false)]
        public string Name { get; set; }

        /// <summary>背景色 "#RRGGBB"；空 = 透明。</summary>
        [DataMember(Name = "bgColor", EmitDefaultValue = false)]
        public string BgColor { get; set; }

        /// <summary>文字色 "#RRGGBB"；空 = 跟随系统文字色。</summary>
        [DataMember(Name = "fontColor", EmitDefaultValue = false)]
        public string FontColor { get; set; }

        /// <summary>音效文件名（如 "chime.wav"）；空 = 静音。</summary>
        [DataMember(Name = "sound", EmitDefaultValue = false)]
        public string Sound { get; set; }

        public TagTemplate()
        {
            Name = string.Empty;
        }

        public TagTemplate(string name, string bgColor, string fontColor, string sound)
        {
            Name = name ?? string.Empty;
            BgColor = bgColor;
            FontColor = fontColor;
            Sound = sound;
        }

        /// <summary>
        /// DataContractJsonSerializer 反序列化不调用构造函数，靠这里兜底。
        /// </summary>
        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            Name = string.Empty;
        }

        public TagTemplate Clone()
        {
            return new TagTemplate(Name, BgColor, FontColor, Sound) { Id = Id };
        }
    }
}