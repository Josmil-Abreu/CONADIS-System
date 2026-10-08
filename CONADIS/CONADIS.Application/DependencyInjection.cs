using System;
using System.Collections.Generic;
using System.Text;
using CONADIS.Application.Interfaces;
using CONADIS.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CONADIS.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<ISolicitudExpedienteService, SolicitudExpedienteService>();
        services.AddScoped<IEvaluacionCifService, EvaluacionCifService>();
        services.AddScoped<IDictamenService, DictamenService>();

        return services;
    }
}