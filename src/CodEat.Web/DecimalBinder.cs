using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Globalization;
namespace CodEat.Web;
public class DecimalBinder : IModelBinder {
 public Task BindModelAsync(ModelBindingContext context){var value=context.ValueProvider.GetValue(context.ModelName);if(value==ValueProviderResult.None)return Task.CompletedTask;context.ModelState.SetModelValue(context.ModelName,value);var raw=value.FirstValue;if(string.IsNullOrWhiteSpace(raw)&&Nullable.GetUnderlyingType(context.ModelType)!=null){context.Result=ModelBindingResult.Success(null);return Task.CompletedTask;}if(decimal.TryParse(raw?.Replace(',','.'),NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var number))context.Result=ModelBindingResult.Success(number);else context.ModelState.TryAddModelError(context.ModelName,"Nombre invalide.");return Task.CompletedTask;}
}
public class DecimalBinderProvider : IModelBinderProvider {public IModelBinder? GetBinder(ModelBinderProviderContext context)=>context.Metadata.ModelType==typeof(decimal)||context.Metadata.ModelType==typeof(decimal?)?new DecimalBinder():null;}
