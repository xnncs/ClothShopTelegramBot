using ShopTelegramBot.Abstract;

namespace ShopTelegramBot.Helpers;

public class CallbackGenerateHelper : ICallbackGenerateHelper
{
    public string GenerateCategoriesCallbackFormatStringOnDelete(string x)
    {
        return $"categories/delete/{x.ToLower()}";
    }

    public string GenerateCategoriesCallbackFormatStringOnGet(string x)
    {
        return $"categories/get/{x.ToLower()}";
    }

    public string GenerateItemsCallbackFormatStringOnGet(string x)
    {
        return $"items/get/{x.ToLower()}";
    }

    public string GenerateItemsCallbackFormatStringOnDelete(string x)
    {
        return $"items/delete/{x.ToLower()}";
    }

    public string GenerateCallbackOnAddToCart(string x)
    {
        return $"items/addToCart/{x.ToLower()}";
    }
}