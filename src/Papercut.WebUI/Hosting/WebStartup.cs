﻿// Papercut
//
// Copyright © 2008 - 2012 Ken Robertson
// Copyright © 2013 - 2017 Jaben Cargman
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.


namespace Papercut.WebUI.Hosting
{
    using System;
    using Microsoft.AspNetCore.Hosting;

    using Autofac;
    
    using System.Threading;
    using Microsoft.Extensions.PlatformAbstractions;
    using Autofac.Extensions.DependencyInjection;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;

    internal class WebStartup
    {
        public static ILifetimeScope Scope { get; set; }
        public static void Start(ushort httpPort, CancellationToken cancellation)
        {
            var hostBuilder = new WebHostBuilder();
            hostBuilder
                .UseWebRoot(PlatformServices.Default.Application.ApplicationBasePath)
                .UseKestrel()
                .UseStartup<WebStartup>()
                .UseUrls($"http://*:{httpPort}");

            var host = hostBuilder.Build();
            host.Run(cancellation);
        }

        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            services.AddLogging();
            services.AddMemoryCache();

            var mvcCore = services.AddMvcCore();
            mvcCore.AddJsonFormatters();


            var builder = new ContainerBuilder();
            builder.Populate(services);

#pragma warning disable CS0618 // Type or member is obsolete
            builder.Update(Scope.ComponentRegistry);
            return new AutofacServiceProvider(Scope);
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            loggerFactory.AddProvider(new SerilogLoggerProvider(Scope));
            app.UseMvc();
            app.UseResponseBuffering();
        }
    }
}