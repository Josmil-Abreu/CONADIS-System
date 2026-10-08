using System;
using System.Collections.Generic;
using System.Text;
using CONADIS.Application.Interfaces;
using CONADIS.Infrastructure.Persistence;
using CONADIS.Infrastructure.Persistence.Repositories;
using CONADIS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CONADIS.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, string? connectionString = null)
    {
        // Si no se pasa cadena de conexión, usamos base de datos en memoria para desarrollo/pruebas rápidas
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<ConadisDbContext>(options =>
                options.UseInMemoryDatabase("CONADIS_InMemoryDb"));
        }
        else
        {
            services.AddDbContext<ConadisDbContext>(options =>
                options.UseSqlServer(connectionString));
        }

        // Repositorios
        services.AddScoped<IExpedienteRepository, ExpedienteRepository>();

        // Servicios externos simulados
        services.AddScoped<IJceIdentityService, JceIdentityService>();
        services.AddScoped<IMspDiagnosticService, MspDiagnosticService>();
        services.AddScoped<IFirmaDigitalService, FirmaDigitalService>();

        return services;
    }
}