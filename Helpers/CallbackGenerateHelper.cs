using System.Text.RegularExpressions;
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

    public string GenerateCallbackOnRemoveFromCart(string x)
    {
        return $"items/removeFromCart/{x.ToLower()}";
    }

    public string GenerateCallbackOnGetFeedbackByPageNumber(int pageNumber)
    {
        return $"feedbacks/get/{pageNumber}";
    }

    public Regex GetOnGetFeedbackByPageNumberRegex()
    {
        Regex feedbackRegex = new Regex("feedbacks/get/([0-9]+)");
        return feedbackRegex;
    }

    public int GetPageNumberByGetFeedbackCallbackString(string x)
    {
        return int.Parse(x.Replace("feedbacks/get/", ""));
    }
}