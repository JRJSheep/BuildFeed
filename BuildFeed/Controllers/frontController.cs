﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using BuildFeed.Code;
using BuildFeed.Model;
using BuildFeed.Model.View;
using OneSignal.CSharp.SDK;
using OneSignal.CSharp.SDK.Resources;
using OneSignal.CSharp.SDK.Resources.Notifications;

namespace BuildFeed.Controllers
{
    public class FrontController : BaseController
    {
        public const int PAGE_SIZE = 72;

        private readonly BuildRepository _bModel;
        private readonly MetaItem _mModel;

        public FrontController()
        {
            _bModel = new BuildRepository();
            _mModel = new MetaItem();
        }

        [Route("", Order = 1)]
#if !DEBUG
      [OutputCache(Duration = 600, VaryByParam = "none", VaryByCustom = "userName;lang;theme")]
      [OutputCachePush(Order = 2)]
#endif
        public async Task<ActionResult> Index()
        {
            FrontPage fp = await _bModel.SelectFrontPage();
            return View("Index", fp);
        }

        [Route("page-{page:int:min(1)}/", Order = 0)]
#if !DEBUG
      [OutputCache(Duration = 600, VaryByParam = "page", VaryByCustom = "userName;lang;theme")]
      [OutputCachePush(Order = 2)]
#endif
        public async Task<ActionResult> IndexPage(int page)
        {
            FrontBuildGroup[] buildGroups = await _bModel.SelectAllGroups(PAGE_SIZE, (page - 1) * PAGE_SIZE);

            ViewBag.PageNumber = page;
            ViewBag.PageCount = Math.Ceiling(Convert.ToDouble(await _bModel.SelectAllGroupsCount()) / Convert.ToDouble(PAGE_SIZE));

            if (ViewBag.PageNumber > ViewBag.PageCount)
            {
                return new HttpNotFoundResult();
            }

            return View("Pages", buildGroups);
        }

        [Route("group/{major}.{minor}.{number}.{revision}/", Order = 1)]
        [Route("group/{major}.{minor}.{number}/", Order = 5)]
        // for when there is no revision
#if !DEBUG
      [OutputCache(Duration = 600, VaryByParam = "none", VaryByCustom = "userName;lang;theme")]
      [OutputCachePush(Order = 2)]
#endif
        public async Task<ActionResult> ViewGroup(uint major, uint minor, uint number, uint? revision = null)
        {
            BuildGroup bg = new BuildGroup
            {
                Major = major,
                Minor = minor,
                Build = number,
                Revision = revision
            };

            List<Build> builds = await _bModel.SelectGroup(bg);

            return builds.Count == 1
                ? RedirectToAction(nameof(ViewBuild),
                    new
                    {
                        id = builds.Single().Id
                    }) as ActionResult
                : View(new Tuple<BuildGroup, List<Build>>(bg, builds));
        }

        [Route("build/{id:guid}/", Name = "Build")]
#if !DEBUG
      [OutputCache(Duration = 600, VaryByParam = "none", VaryByCustom = "userName;lang;theme")]
      [OutputCachePush(Order = 2)]
#endif
        public async Task<ActionResult> ViewBuild(Guid id)
        {
            Build b = await _bModel.SelectById(id);
            if (b == null)
            {
                return new HttpNotFoundResult();
            }

            return View(b);
        }

        [Route("build/{id:long}/", Name = "Build (Legacy)")]
        public async Task<ActionResult> ViewBuild(long id)
        {
            Build b = await _bModel.SelectByLegacyId(id);
            if (b == null)
            {
                return new HttpNotFoundResult();
            }

            return RedirectToAction(nameof(ViewBuild),
                new
                {
                    id = b.Id
                });
        }

        [Route("twitter/{id:guid}/", Name = "Twitter")]
#if !DEBUG
      [OutputCache(Duration = 600, VaryByParam = "none", VaryByCustom = "userName;lang;theme")]
      [CustomContentType(ContentType = "image/png", Order = 2)]
#endif
        public async Task<ActionResult> TwitterCard(Guid id)
        {
            Build b = await _bModel.SelectById(id);
            if (b == null)
            {
                return new HttpNotFoundResult();
            }

            string path = Path.Combine(Server.MapPath("~/res/card/"), $"{b.Family}.png");
            bool backExists = System.IO.File.Exists(path);

            using (Bitmap bm = backExists
                ? new Bitmap(path)
                : new Bitmap(1120, 600))
            {
                using (Graphics gr = Graphics.FromImage(bm))
                {
                    gr.CompositingMode = CompositingMode.SourceOver;
                    gr.CompositingQuality = CompositingQuality.HighQuality;
                    gr.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    gr.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                    gr.SmoothingMode = SmoothingMode.HighQuality;
                    gr.PixelOffsetMode = PixelOffsetMode.HighQuality;

                    if (!backExists)
                    {
                        gr.FillRectangle(new SolidBrush(Color.FromArgb(0x24, 0x24, 0x23)), 0, 0, 1120, 600);
                    }

                    int left = 40;
                    using (GraphicsPath gp = new GraphicsPath())
                    {
                        foreach (char c in "BUILDFEED")
                        {
                            gp.AddString(c.ToString(), new FontFamily("Segoe UI Semibold"), 0, 32, new Point(left, 32), StringFormat.GenericTypographic);

                            RectangleF bounds = gp.GetBounds();
                            left = Convert.ToInt32(bounds.Width);
                            left += 52;
                        }

                        gr.FillPath(Brushes.White, gp);
                    }

                    using (GraphicsPath gp = new GraphicsPath())
                    {
                        gp.AddString(b.Number.ToString(), new FontFamily("Segoe UI Light"), 0, 260, new Point(32, 114), StringFormat.GenericTypographic);

                        RectangleF bounds = gp.GetBounds();
                        left = Convert.ToInt32(bounds.Width);
                        left += 44;

                        if (b.Revision.HasValue)
                        {
                            gp.AddString($".{b.Revision}", new FontFamily("Segoe UI Light"), 0, 160, new Point(left, 220), StringFormat.GenericTypographic);
                        }

                        gr.DrawPath(new Pen(new SolidBrush(Color.FromArgb(0x24, 0x24, 0x23)), 4), gp);
                        gr.FillPath(Brushes.White, gp);
                    }

                    using (GraphicsPath gp = new GraphicsPath())
                    {
                        gp.AddString($"{MvcExtensions.GetDisplayTextForEnum(b.Family)} (NT {b.MajorVersion}.{b.MinorVersion})", new FontFamily("Segoe UI Light"), 0, 48, new Point(40, 80), StringFormat.GenericTypographic);

                        gp.AddString(char.ConvertFromUtf32(0xf126), new FontFamily("FontAwesome"), 0, 28, new Point(46, 468), StringFormat.GenericTypographic);
                        gp.AddString(b.Lab, new FontFamily("Segoe UI Light"), 0, 40, new Point(88, 450), StringFormat.GenericTypographic);

                        if (b.BuildTime.HasValue)
                        {
                            gp.AddString(char.ConvertFromUtf32(0xf017), new FontFamily("FontAwesome"), 0, 28, new Point(40, 538), StringFormat.GenericTypographic);
                            gp.AddString($"{b.BuildTime.Value.ToShortTimeString()} on {b.BuildTime.Value.ToLongDateString()}", new FontFamily("Segoe UI Light"), 0, 40, new Point(88, 520), StringFormat.GenericTypographic);
                        }

                        gr.FillPath(Brushes.White, gp);
                    }

                    Response.ContentType = "image/png";
                    bm.Save(Response.OutputStream, ImageFormat.Png);
                }
            }

            return new EmptyResult();
        }

        [Route("twitter/{id:long}/", Name = "Twitter (Legacy)")]
        public async Task<ActionResult> TwitterCard(long id)
        {
            Build b = await _bModel.SelectByLegacyId(id);
            if (b == null)
            {
                return new HttpNotFoundResult();
            }

            return RedirectToAction(nameof(TwitterCard),
                new
                {
                    id = b.Id
                });
        }

        [Route("lab/{lab}/", Order = 1, Name = "Lab Root")]
#if !DEBUG
      [OutputCache(Duration = 600, VaryByParam = "none", VaryByCustom = "userName;lang;theme")]
      [OutputCachePush(Order = 2)]
#endif
        public async Task<ActionResult> ViewLab(string lab)
        {
            return await ViewLabPage(lab, 1);
        }

        [Route("lab/{lab}/page-{page:int:min(2)}/", Order = 0)]
#if !DEBUG
      [OutputCache(Duration = 600, VaryByParam = "page", VaryByCustom = "userName;lang;theme")]
      [OutputCachePush(Order = 2)]
#endif
        public async Task<ActionResult> ViewLabPage(string lab, int page)
        {
            ViewBag.MetaItem = await _mModel.SelectById(new MetaItemKey
            {
                Type = MetaType.Lab,
                Value = lab
            });

            List<Build> builds = await _bModel.SelectLab(lab, PAGE_SIZE, (page - 1) * PAGE_SIZE);

            ViewBag.ItemId = builds.FirstOrDefault()?.Lab;
            ViewBag.PageNumber = page;
            ViewBag.PageCount = Math.Ceiling(Convert.ToDouble(await _bModel.SelectLabCount(lab)) / Convert.ToDouble(PAGE_SIZE));

            if (ViewBag.PageNumber > ViewBag.PageCount)
            {
                return new HttpNotFoundResult();
            }

            return View("viewLab", builds);
        }

        [Route("source/{source}/", Order = 1, Name = "Source Root")]
#if !DEBUG
      [OutputCache(Duration = 600, VaryByParam = "none", VaryByCustom = "userName;lang;theme")]
      [OutputCachePush(Order = 2)]
#endif
        public async Task<ActionResult> ViewSource(TypeOfSource source)
        {
            return await ViewSourcePage(source, 1);
        }

        [Route("source/{source}/page-{page:int:min(2)}/", Order = 0)]
#if !DEBUG
      [OutputCache(Duration = 600, VaryByParam = "page", VaryByCustom = "userName;lang;theme")]
      [OutputCachePush(Order = 2)]
#endif
        public async Task<ActionResult> ViewSourcePage(TypeOfSource source, int page)
        {
            ViewBag.MetaItem = await _mModel.SelectById(new MetaItemKey
            {
                Type = MetaType.Source,
                Value = source.ToString()
            });
            ViewBag.ItemId = MvcExtensions.GetDisplayTextForEnum(source);

            List<Build> builds = await _bModel.SelectSource(source, PAGE_SIZE, (page - 1) * PAGE_SIZE);

            ViewBag.PageNumber = page;
            ViewBag.PageCount = Math.Ceiling(Convert.ToDouble(await _bModel.SelectSourceCount(source)) / Convert.ToDouble(PAGE_SIZE));

            if (ViewBag.PageNumber > ViewBag.PageCount)
            {
                return new HttpNotFoundResult();
            }

            return View("viewSource", builds);
        }

        [Route("year/{year}/", Order = 1, Name = "Year Root")]
#if !DEBUG
      [OutputCache(Duration = 600, VaryByParam = "none", VaryByCustom = "userName;lang;theme")]
      [OutputCachePush(Order = 2)]
#endif
        public async Task<ActionResult> ViewYear(int year)
        {
            return await ViewYearPage(year, 1);
        }

        [Route("year/{year}/page-{page:int:min(2)}/", Order = 0)]
#if !DEBUG
      [OutputCache(Duration = 600, VaryByParam = "page", VaryByCustom = "userName;lang;theme")]
      [OutputCachePush(Order = 2)]
#endif
        public async Task<ActionResult> ViewYearPage(int year, int page)
        {
            ViewBag.MetaItem = await _mModel.SelectById(new MetaItemKey
            {
                Type = MetaType.Year,
                Value = year.ToString()
            });
            ViewBag.ItemId = year.ToString();

            List<Build> builds = await _bModel.SelectYear(year, PAGE_SIZE, (page - 1) * PAGE_SIZE);

            ViewBag.PageNumber = page;
            ViewBag.PageCount = Math.Ceiling(await _bModel.SelectYearCount(year) / Convert.ToDouble(PAGE_SIZE));

            if (ViewBag.PageNumber > ViewBag.PageCount)
            {
                return new HttpNotFoundResult();
            }

            return View("viewYear", builds);
        }

        [Route("version/{major}.{minor}/", Order = 1, Name = "Version Root")]
#if !DEBUG
      [OutputCache(Duration = 600, VaryByParam = "none", VaryByCustom = "userName;lang;theme")]
      [OutputCachePush(Order = 2)]
#endif
        public async Task<ActionResult> ViewVersion(uint major, uint minor)
        {
            return await ViewVersionPage(major, minor, 1);
        }

        [Route("version/{major}.{minor}/page-{page:int:min(2)}/", Order = 0)]
#if !DEBUG
      [OutputCache(Duration = 600, VaryByParam = "none", VaryByCustom = "userName;lang;theme")]
      [OutputCachePush(Order = 2)]
#endif
        public async Task<ActionResult> ViewVersionPage(uint major, uint minor, int page)
        {
            string valueString = $"{major}.{minor}";
            ViewBag.MetaItem = await _mModel.SelectById(new MetaItemKey
            {
                Type = MetaType.Version,
                Value = valueString
            });
            ViewBag.ItemId = valueString;

            List<Build> builds = await _bModel.SelectVersion(major, minor, PAGE_SIZE, (page - 1) * PAGE_SIZE);

            ViewBag.PageNumber = page;
            ViewBag.PageCount = Math.Ceiling(Convert.ToDouble(await _bModel.SelectVersionCount(major, minor)) / Convert.ToDouble(PAGE_SIZE));

            if (ViewBag.PageNumber > ViewBag.PageCount)
            {
                return new HttpNotFoundResult();
            }

            return View("viewVersion", builds);
        }

        [Route("add/")]
        [Authorize]
        public ActionResult AddBuild()
        {
            Build b = new Build
            {
                SourceType = TypeOfSource.PrivateLeak
            };
            return View("EditBuild", b);
        }

        [Route("add/")]
        [Authorize]
        [HttpPost]
        public async Task<ActionResult> AddBuild(Build build)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    build.Added = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Utc);
                    build.Modified = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Utc);
                    if (build.BuildTime.HasValue)
                    {
                        build.BuildTime = DateTime.SpecifyKind(build.BuildTime.Value, DateTimeKind.Utc);
                    }

                    if (build.LeakDate.HasValue)
                    {
                        build.LeakDate = DateTime.SpecifyKind(build.LeakDate.Value, DateTimeKind.Utc);
                    }

                    await _bModel.Insert(build);
                }
                catch
                {
                    return View("EditBuild", build);
                }

                OneSignalClient osc = new OneSignalClient(ConfigurationManager.AppSettings["push:OneSignalApiKey"]);
                osc.Notifications.Create(new NotificationCreateOptions
                {
                    AppId = Guid.Parse(ConfigurationManager.AppSettings["push:AppId"]),
                    IncludedSegments = new List<string>
                    {
#if DEBUG
                        "Testers"
#else
                        "All"
#endif
                    },
                    Headings =
                    {
                        {LanguageCodes.English, "A new build has been added to BuildFeed!"}
                    },
                    Contents =
                    {
                        {LanguageCodes.English, build.AlternateBuildString}
                    },
                    Url = $"https://buildfeed.net{Url.Action(nameof(ViewBuild), new { id = build.Id })}?utm_source=notification&utm_campaign=new_build"
                });

                return RedirectToAction(nameof(ViewBuild),
                    new
                    {
                        id = build.Id
                    });
            }
            return View("EditBuild", build);
        }

        [Route("edit/{id}/")]
        [Authorize]
        public async Task<ActionResult> EditBuild(Guid id)
        {
            Build b = await _bModel.SelectById(id);
            return View(b);
        }

        [Route("edit/{id}/")]
        [Authorize]
        [HttpPost]
        public async Task<ActionResult> EditBuild(Guid id, Build build)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    if (build.BuildTime.HasValue)
                    {
                        build.BuildTime = DateTime.SpecifyKind(build.BuildTime.Value, DateTimeKind.Utc);
                    }

                    if (build.LeakDate.HasValue)
                    {
                        build.LeakDate = DateTime.SpecifyKind(build.LeakDate.Value, DateTimeKind.Utc);
                    }

                    await _bModel.Update(build);
                }
                catch
                {
                    return View(build);
                }

                return RedirectToAction(nameof(ViewBuild),
                    new
                    {
                        id = build.Id
                    });
            }
            return View(build);
        }

        [Route("delete/{id}/")]
        [Authorize(Roles = "Administrators")]
        public async Task<ActionResult> DeleteBuild(Guid id)
        {
            await _bModel.DeleteById(id);
            return RedirectToAction(nameof(Index));
        }
    }
}