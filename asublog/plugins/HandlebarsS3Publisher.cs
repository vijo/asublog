namespace Asublog.Plugins
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Security.Cryptography;
    using System.Text;
    using Amazon;
    using Amazon.CloudFront;
    using Amazon.CloudFront.Model;
    using Amazon.S3;
    using Amazon.S3.Model;
    using Amazon.S3.Transfer;
    using HandlebarsDotNet;
    using Core;

    public class HandlebarsS3Publisher : PublishingPlugin
    {
        private static readonly string _basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "templates");
        private string _assetPath;
        private Func<object, string> _index;
        private Func<object, string> _post;
        private int _postsPerPage;
        private AmazonS3Client _client;
        private string _bucket;

        public HandlebarsS3Publisher() : base("handlebarsS3Publisher", "0.5") { }

        public override void Init()
        {
            _bucket = Config["bucket"];
            var theme = Config["theme"];
            _postsPerPage = int.Parse(Config["postsPerPage"]);

            if(string.IsNullOrEmpty(theme))
                throw new ArgumentException("No theme was specified");

            var themePath = Path.Combine(_basePath, theme);
            _assetPath = Path.Combine(themePath, "assets");

            var index = Path.Combine(themePath, "index.handlebars");
            if(!File.Exists(index)) throw new FileNotFoundException("Could not find index template", index);
            _index = Handlebars.Compile(File.ReadAllText(index));

            var post = Path.Combine(themePath, "post.handlebars");
            if(File.Exists(post))
            {
                _post = Handlebars.Compile(File.ReadAllText(post));
            }
            else
            {
                Log.Info("Post template not found, post pages will not be generated.");
            }

            _client = new AmazonS3Client(Config["awsKey"], Config["awsSecret"],
                RegionEndpoint.GetBySystemName(Config["awsRegion"]));
        }

        private string SaveIndex(Post[] posts, int pageNum, int maxPage)
        {
            var fname = string.Format("index{0}.html", pageNum > 0 ? pageNum.ToString() : null);

            var content = _index(new
            {
                posts,
                page = new
                {
                    num = pageNum,
                    max = maxPage,
                    isFirst = pageNum == 0,
                    isLast = pageNum == maxPage
                }
            });

            Log.Debug(string.Format("Rendered index {0}", fname), content);
            return UploadContent(fname, content);
        }

        private string SavePost(Post post)
        {
            if(_post == null) return null;

            var fname = string.Format("posts/{0}.html", post.Id);
            var content = _post(post);

            Log.Debug(string.Format("Rendered post {0}", fname), content);
            return UploadContent(fname, content);
        }

        private string Md5ToStr(byte[] hash)
        {
            var sb = new StringBuilder();
            for(var i = 0; i < hash.Length; i++)
            {
                sb.Append(hash[i].ToString("x2"));
            }
            return sb.ToString();
        }

        private string GetETag(string key)
        {
            try
            {
                var headReq = new GetObjectMetadataRequest
                {
                    BucketName = _bucket,
                    Key = key
                };

                var head = _client.GetObjectMetadata(headReq);
                return head.ETag.Trim(new[] {'"'});
            }
            catch(AmazonS3Exception ex)
            {
                if(ex.StatusCode != HttpStatusCode.NotFound) throw;
            }
            return null;
        }

        private string UploadContent(string fname, string content)
        {
            string md5;
            using(var md5er = MD5.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(content);
                var hash = md5er.ComputeHash(bytes);
                md5 = Md5ToStr(hash);
            }

            var etag = GetETag(fname);

            Log.Debug(string.Format("Content md5 {0}, S3 etag {1}", md5, etag));
            if(!md5.Equals(etag, StringComparison.OrdinalIgnoreCase))
            {
                var putReq = new PutObjectRequest
                {
                    BucketName = _bucket,
                    Key = fname,
                    ContentBody = content
                };

                var resp = _client.PutObject(putReq);
                if(resp.HttpStatusCode == HttpStatusCode.OK)
                {
                    Log.Info(string.Format("Uploaded s3://{0}/{1}", _bucket, fname));
                }
                else
                {
                    Log.Error(string.Format("Got unexpected result when uploading s3://{0}/{1}: {2}", _bucket, fname, resp.HttpStatusCode));
                }
                return string.Format("/{0}", fname);
            }
            else
            {
                Log.Debug(string.Format("Skipping s3://{0}/{1} (file unchanged)", _bucket, fname));
            }
            return null;
        }

        private string UploadFile(string path, string dir)
        {
            string md5;
            using(var md5er = MD5.Create())
            {
                using(var stream = File.OpenRead(path))
                {
                    var hash = md5er.ComputeHash(stream);
                    md5 = Md5ToStr(hash);
                }
            }

            var key = string.Format("{0}/{1}", dir, Path.GetFileName(path));
            var etag = GetETag(key);

            if(!md5.Equals(etag, StringComparison.OrdinalIgnoreCase))
            {
                var putReq = new PutObjectRequest
                {
                    BucketName = _bucket,
                    Key = key,
                    FilePath = path
                };

                var resp = _client.PutObject(putReq);
                if(resp.HttpStatusCode == HttpStatusCode.OK)
                {
                    Log.Info(string.Format("Uploaded s3://{0}/{1}", _bucket, key));
                    return string.Format("/{0}", key);
                }
                else
                {
                    Log.Error(string.Format("Got unexpected result when uploading s3://{0}/{1}: {2}", _bucket, key, resp.HttpStatusCode));
                }
            }
            else
            {
                Log.Debug(string.Format("Skipping s3://{0}/{1} (file unchanged)", _bucket, key));
            }
            return null;
        }

        private List<string> UploadDirectory(string path, string dir)
        {
            Log.Debug(string.Format("Uploading directory {0} ({1})", path, dir));
            var uploaded = new List<string>();
            var files = Directory.GetFiles(path);
            foreach(var file in files)
            {
                var fpath = Path.Combine(path, file);
                string key;
                if(!string.IsNullOrEmpty((key = UploadFile(fpath, dir))))
                    uploaded.Add(key);
            }
            var folders = Directory.GetDirectories(path);
            foreach(var folder in folders)
            {
                var newdir = string.Format("{0}/{1}", dir, Path.GetFileName(folder));
                var dpath = Path.Combine(path, folder);
                uploaded.AddRange(UploadDirectory(dpath, newdir));
            }
            return uploaded;
        }

        public override void Publish(IEnumerator<Post> posts, int count)
        {
            var invalidations = new List<string>();

            var maxPage = (int) Math.Ceiling((float) count / _postsPerPage) - 1;
            var page = 0;
            var pagePosts = new List<Post>();
            while(posts.MoveNext())
            {
                invalidations.Add(SavePost(posts.Current));
                pagePosts.Add(posts.Current);
                if(pagePosts.Count == _postsPerPage)
                {
                    invalidations.Add(SaveIndex(pagePosts.ToArray(), page++, maxPage));
                    pagePosts.Clear();
                }
            }
            if(pagePosts.Count > 0)
            {
                invalidations.Add(SaveIndex(pagePosts.ToArray(), page, maxPage));
            }

            var invalidPaths = invalidations.Where(i => !string.IsNullOrEmpty(i)).ToList();

            invalidPaths.AddRange(UploadDirectory(_assetPath, "assets"));

            if(invalidPaths.Count > 0)
            {
                var distId = Config["cloudfrontDistId"];
                if(distId != null)
                {
                    var client = new AmazonCloudFrontClient(Config["awsKey"], Config["awsSecret"],
                        RegionEndpoint.GetBySystemName(Config["awsRegion"]));

                    var invReq = new CreateInvalidationRequest
                    {
                        DistributionId = distId,
                        InvalidationBatch = new InvalidationBatch
                        {
                            Paths = new Paths
                            {
                                Quantity = invalidPaths.Count,
                                Items = invalidPaths
                            },
                            CallerReference = DateTime.Now.Ticks.ToString()
                        }
                    };

                    var resp = client.CreateInvalidation(invReq);
                    if(resp.HttpStatusCode == HttpStatusCode.Created)
                    {
                        Log.Info(string.Format("Created invalidation for Cloudfront Distribution {0}", distId));
                        Log.Debug("Invalidated Paths", invalidPaths);
                    }
                    else
                    {
                        Log.Error(string.Format("Got unexpected result creating invalidation for Cloudfront Distribution {0}: {1}", distId, resp.HttpStatusCode));
                    }
                }
            }
        }
    }
}
