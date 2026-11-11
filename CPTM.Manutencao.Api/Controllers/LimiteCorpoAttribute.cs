using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CPTM.Manutencao.Api.Controllers
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class LimiteCorpoAttribute : Attribute, IResourceFilter
    {
        private readonly long _limite;
        private readonly string _mensagem;

        public LimiteCorpoAttribute(long limite, string mensagem)
        {
            _limite = limite;
            _mensagem = mensagem;
        }

        public void OnResourceExecuting(ResourceExecutingContext context)
        {
            if (context.HttpContext.Request.ContentLength > _limite)
                context.Result = new BadRequestObjectResult(new { erro = _mensagem });
        }

        public void OnResourceExecuted(ResourceExecutedContext context) { }
    }
}
