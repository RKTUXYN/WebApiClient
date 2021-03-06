﻿using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;

namespace WebApiClient.Contexts
{
    /// <summary>
    /// 表示请求Api的上下文
    /// </summary>
    public class ApiActionContext
    {
        /// <summary>
        /// 自定义数据的存储和访问容器
        /// </summary>
        private Tags tags;

        /// <summary>
        /// 请求取消令牌集合
        /// </summary>
        private IList<CancellationToken> cancellationTokens;

        /// <summary>
        /// 获取本次请求相关的自定义数据的存储和访问容器
        /// </summary>
        public Tags Tags
        {
            get
            {
                if (this.tags == null)
                {
                    this.tags = new Tags();
                }
                return this.tags;
            }
        }

        /// <summary>
        /// 获取请求取消令牌集合
        /// 这些令牌将被连接起来
        /// </summary>
        public IList<CancellationToken> CancellationTokens
        {
            get
            {
                if (this.cancellationTokens == null)
                {
                    this.cancellationTokens = new List<CancellationToken>();
                }
                return this.cancellationTokens;
            }
        }


        /// <summary>
        /// 获取关联的HttpApiConfig
        /// </summary>
        public HttpApiConfig HttpApiConfig { get; internal set; }

        /// <summary>
        /// 获取关联的ApiActionDescriptor
        /// </summary>
        public ApiActionDescriptor ApiActionDescriptor { get; internal set; }

        /// <summary>
        /// 获取关联的HttpRequestMessage
        /// </summary>
        public HttpApiRequestMessage RequestMessage { get; internal set; }

        /// <summary>
        /// 获取关联的HttpResponseMessage
        /// </summary>
        public HttpResponseMessage ResponseMessage { get; internal set; }

        /// <summary>
        /// 获取调用Api得到的结果
        /// </summary>
        public object Result { get; internal set; }

        /// <summary>
        /// 获取调用Api产生的异常
        /// </summary>
        public Exception Exception { get; internal set; }

        /// <summary>
        /// 执行Api方法
        /// </summary>
        /// <returns></returns>
        public async Task<TResult> ExecuteActionAsync<TResult>()
        {
            await this.PrepareRequestAsync().ConfigureAwait(false);
            await this.ExecFiltersAsync(filter => filter.OnBeginRequestAsync).ConfigureAwait(false);
            await this.ExecRequestAsync().ConfigureAwait(false);
            await this.ExecFiltersAsync(filter => filter.OnEndRequestAsync).ConfigureAwait(false);

            if (this.Exception == null)
            {
                return (TResult)this.Result;
            }
            throw this.Exception;
        }

        /// <summary>
        /// 准备请求数据
        /// </summary>
        /// <returns></returns>
        private async Task PrepareRequestAsync()
        {
            var apiAction = this.ApiActionDescriptor;
            var validateProperty = this.HttpApiConfig.UseParameterPropertyValidate;

            foreach (var parameter in apiAction.Parameters)
            {
                ParameterValidator.Validate(parameter, validateProperty);
            }

            foreach (var actionAttribute in apiAction.Attributes)
            {
                await actionAttribute.BeforeRequestAsync(this).ConfigureAwait(false);
            }

            foreach (var parameter in apiAction.Parameters)
            {
                foreach (var parameterAttribute in parameter.Attributes)
                {
                    await parameterAttribute.BeforeRequestAsync(this, parameter).ConfigureAwait(false);
                }
            }

            await apiAction.Return.Attribute.BeforeRequestAsync(this);
        }

        /// <summary>
        /// 执行请求
        /// </summary>
        /// <returns></returns>
        private async Task ExecRequestAsync()
        {
            using (var cancellation = this.CreateLinkedTokenSource())
            {
                try
                {
                    this.ResponseMessage = await this.HttpApiConfig.HttpClient
                        .SendAsync(this.RequestMessage, cancellation.Token)
                        .ConfigureAwait(false);

                    this.Result = await this.ApiActionDescriptor.Return.Attribute
                        .GetTaskResult(this)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    this.Exception = ex;
                }
            }
        }

        /// <summary>
        /// 创建取消令牌源
        /// </summary>
        /// <returns></returns>
        private CancellationTokenSource CreateLinkedTokenSource()
        {
            if (this.cancellationTokens == null || this.cancellationTokens.Count == 0)
            {
                return CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
            }
            else
            {
                var tokens = this.cancellationTokens.ToArray();
                return CancellationTokenSource.CreateLinkedTokenSource(tokens);
            }
        }

        /// <summary>
        /// 执行所有过滤器
        /// </summary>
        /// <param name="funcSelector">方法选择</param>
        /// <returns></returns>
        private async Task ExecFiltersAsync(Func<IApiActionFilter, Func<ApiActionContext, Task>> funcSelector)
        {
            foreach (var filter in this.HttpApiConfig.GlobalFilters)
            {
                await funcSelector(filter)(this).ConfigureAwait(false);
            }

            foreach (var filter in this.ApiActionDescriptor.Filters)
            {
                await funcSelector(filter)(this).ConfigureAwait(false);
            }
        }
    }
}
