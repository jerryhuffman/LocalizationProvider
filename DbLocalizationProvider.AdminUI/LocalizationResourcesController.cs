﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web;
using System.Web.Mvc;
using DbLocalizationProvider.AdminUI.Queries;
using DbLocalizationProvider.Export;
using DbLocalizationProvider.Import;
using DbLocalizationProvider.Queries;

namespace DbLocalizationProvider.AdminUI
{
    public class JsonServiceResult
    {
        public string Message { get; set; }
    }

    [AuthorizeRoles]
    public class LocalizationResourcesController : Controller
    {
        private readonly string _cookieName = ".DbLocalizationProvider-SelectedLanguages";
        private readonly ILocalizationResourceRepository _resourceRepository;

        public LocalizationResourcesController()
        {
            _resourceRepository = ConfigurationContext.Current.Repository;
        }

        public ActionResult Index()
        {
            return View(PrepareViewModel(false));
        }

        public ActionResult Main()
        {
            return View("Index", PrepareViewModel(true));
        }

        private LocalizationResourceViewModel PrepareViewModel(bool showMenu)
        {
            var availableLanguagesQuery = new GetAvailableLanguages.Query();
            var languages = availableLanguagesQuery.Execute();

            var allResources = GetAllResources();

            var user = HttpContext.User;
            var isAdmin = user.Identity.IsAuthenticated && UiConfigurationContext.Current.AuthorizedAdminRoles.Any(r => user.IsInRole(r));

            return new LocalizationResourceViewModel(allResources, languages, GetSelectedLanguages())
                   {
                       ShowMenu = showMenu,
                       AdminMode = isAdmin
                   };
        }

        [HttpPost]
        public JsonResult Create([Bind(Prefix = "pk")] string resourceKey)
        {
            try
            {
                _resourceRepository.CreateResource(resourceKey, HttpContext.User.Identity.Name, fromCode: false);
                return Json("");
            }
            catch (Exception e)
            {
                Response.StatusCode = (int) HttpStatusCode.InternalServerError;
                return Json(new JsonServiceResult
                            {
                                Message = e.Message
                            });
            }
        }

        [HttpPost]
        public ActionResult Delete([Bind(Prefix = "pk")] string resourceKey, string returnUrl)
        {
            try
            {
                _resourceRepository.DeleteResource(resourceKey);
                return Redirect(returnUrl);
            }
            catch (Exception e)
            {
                Response.StatusCode = (int) HttpStatusCode.InternalServerError;
                return Json(new JsonServiceResult
                            {
                                Message = e.Message
                            });
            }
        }

        [HttpPost]
        [ValidateInput(false)]
        public JsonResult Update([Bind(Prefix = "pk")] string resourceKey,
                                 [Bind(Prefix = "value")] string newValue,
                                 [Bind(Prefix = "name")] string language)
        {
            _resourceRepository.CreateOrUpdateTranslation(resourceKey, new CultureInfo(language), newValue);

            return Json("");
        }

        [HttpPost]
        public ActionResult UpdateLanguages(string[] languages)
        {
            // issue cookie to store selected languages
            WriteSelectedLanguages(languages);

            return RedirectToAction("Index");
        }

        public FileResult ExportResources()
        {
            var stream = new MemoryStream();
            var writer = new StreamWriter(stream, Encoding.UTF8);
            var serializer = new JsonDataSerializer();

            var resources = _resourceRepository.GetAllResources();
            writer.Write(serializer.Serialize(resources));
            writer.Flush();
            stream.Position = 0;

            return File(stream, "application/json", $"localization-resources-{DateTime.Now.ToString("yyyyMMdd")}.json");
        }

        [AuthorizeRoles(Mode = UiContextMode.Admin)]
        public ViewResult ImportResources(bool? showMenu)
        {
            return View("ImportResources",
                        new ImportResourcesViewModel
                        {
                            ShowMenu = showMenu ?? false
                        });
        }

        [HttpPost]
        [AuthorizeRoles(Mode = UiContextMode.Admin)]
        public ViewResult ImportResources(bool? importOnlyNewContent, HttpPostedFileBase importFile, bool? showMenu)
        {
            var model = new ImportResourcesViewModel
                        {
                            ShowMenu = showMenu ?? false
                        };

            if(importFile == null || importFile.ContentLength == 0)
            {
                return View("ImportResources", model);
            }

            var fileInfo = new FileInfo(importFile.FileName);
            if(fileInfo.Extension.ToLower() != ".json")
            {
                ModelState.AddModelError("file", "Uploaded file has different extension. Json file expected");
                return View("ImportResources", model);
            }

            var importer = new ResourceImporter(_resourceRepository);
            var serializer = new JsonDataSerializer();
            var streamReader = new StreamReader(importFile.InputStream);
            var fileContent = streamReader.ReadToEnd();

            try
            {
                var newResources = serializer.Deserialize<IEnumerable<LocalizationResource>>(fileContent);
                var result = importer.Import(newResources, importOnlyNewContent ?? true);

                ViewData["LocalizationProvider_ImportResult"] = result;
            }
            catch (Exception e)
            {
                ModelState.AddModelError("importFailed", $"Import failed! Reason: {e.Message}");
            }

            return View("ImportResources", model);
        }

        private IEnumerable<string> GetSelectedLanguages()
        {
            var cookie = Request.Cookies[_cookieName];
            return cookie?.Value?.Split(new[]
                                        {
                                            "|"
                                        },
                                        StringSplitOptions.RemoveEmptyEntries);
        }

        private List<ResourceListItem> GetAllResources()
        {
            var result = new List<ResourceListItem>();

            var resources = _resourceRepository.GetAllResources()
                                               .OrderBy(r => r.ResourceKey);

            foreach (var resource in resources)
            {
                result.Add(new ResourceListItem(
                               resource.ResourceKey,
                               resource.Translations.Select(t => new ResourceItem(resource.ResourceKey,
                                                                                  t.Value,
                                                                                  new CultureInfo(t.Language))).ToArray(),
                               !resource.FromCode));
            }

            return result;
        }

        private void WriteSelectedLanguages(IEnumerable<string> languages)
        {
            var cookie = new HttpCookie(_cookieName,
                                        string.Join("|", languages ?? new[] { string.Empty }))
                         {
                             HttpOnly = true
                         };
            Response.Cookies.Add(cookie);
        }
    }
}
