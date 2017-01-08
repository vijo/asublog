namespace Asublog.Core
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public class Attachment
    {
        public string Type { get; set; }
        public string Url { get; set; }
        public string Content { get; set; }
    }

    public class Post
    {
        public Guid Id { get; set; }
        public DateTime Created { get; set; }
        public string Content { get; set; }

        public bool Processed { get; set; }

        private List<Attachment> _attachments { get; set; }
        public Attachment[] Attachments
        {
            get { return _attachments.ToArray(); }
        }
        public Attachment[] Images
        {
            get { return _attachments.Where(a => a.Type == "image").ToArray(); }
        }
        public void Attach(string type, string url, string content)
        {
            _attachments.Add(new Attachment
            {
                Type = type,
                Url = url,
                Content = content
            });
        }

        public Post()
        {
            Id = Guid.NewGuid();
            Created = DateTime.UtcNow;
            _attachments = new List<Attachment>();
        }
    }
}
