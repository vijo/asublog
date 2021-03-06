namespace Asublog.Plugins
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Core;

    public class HtmlizeProcessor : ProcessingPlugin
    {
        private static readonly Regex _newlines =
            new Regex(@"(\r?\n)", RegexOptions.Compiled);

        public HtmlizeProcessor() : base("htmlizeProcessor", "0.5") { }

        private string Process(string content)
        {
            string processed = content;

            var urls = PostUtils.UrlRegex.Matches(content);
            var linked = new List<string>();
            foreach(Match match in urls)
            {
                var url = PostUtils.DemobilizeUrl(PostUtils.NormalizeUrl(match.Value));
                if(!linked.Contains(url))
                {
                    var noproto = PostUtils.SanitizeUrl(match.Value);

                    processed = processed.Replace(match.Value,
                        string.Format("<a href='{0}'>{1}</a>", url, noproto));

                    linked.Add(url);
                }
            }

            // line breaks
            if(Config["allowNewlines"] == "true")
            {
                processed = _newlines.Replace(processed, "<br>");
            }

            return processed;
        }

        public override void Process(Post post)
        {
            Log.Debug("Htmlizing post");
            post.Content = Process(post.Content);
            foreach(var attachment in post.Attachments.Where(a => a.ShouldProcess))
            {
                attachment.Content = Process(attachment.Content);
            }
        }
    }
}
