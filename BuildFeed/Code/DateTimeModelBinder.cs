﻿using System;
using System.Globalization;
using System.Web.Mvc;

namespace BuildFeed.Code
{
    public class DateTimeModelBinder : DefaultModelBinder
    {
        public override object BindModel(ControllerContext controllerContext, ModelBindingContext bindingContext)
        {
            ValueProviderResult value = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);

            bool success = DateTime.TryParse(value.AttemptedValue,
                CultureInfo.CurrentUICulture.DateTimeFormat,
                DateTimeStyles.AllowWhiteSpaces,
                out DateTime retValue);

            if (!success)
            {
                success = DateTime.TryParseExact(value.AttemptedValue,
                    "yyMMdd-HHmm",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out retValue);
            }

            return success
                ? retValue as DateTime?
                : null;
        }
    }
}