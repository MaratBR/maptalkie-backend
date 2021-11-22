using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MapTalkieDB
{
    public class PrivateConversation
    {
        public int Id { get; set; }
        public string UserLowerId { get; set; } = string.Empty;
        public string UserHigherId { get; set; } = string.Empty;
        public User UserLower { get; set; } = default!;
        public User UserHigher { get; set; } = default!;

        public DateTime HiddenBoundForLowerUser { get; set; } = DateTime.MinValue;
        public DateTime HiddenBoundForHigherUser { get; set; } = DateTime.MinValue;

        [IgnoreDataMember] public ICollection<PrivateMessage> PrivateMessages { get; set; }
    }
}