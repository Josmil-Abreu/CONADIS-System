using System;
using System.Collections.Generic;
using System.Text;
using CONADIS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CONADIS.Infrastructure.Persistence;

public class ConadisDbContext : DbContext
{
    public ConadisDbContext(DbContextOptions<ConadisDbContext> options) : base(options) { }

    public DbSet<PersonaConDiscapacidad> Personas => Set<PersonaConDiscapacidad>();
    public DbSet<Expediente> Expedientes => Set<Expediente>();
    public DbSet<EvaluacionCIF> Evaluaciones => Set<EvaluacionCIF>();
    public DbSet<Carnet> Carnets => Set<Carnet>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<PersonaConDiscapacidad>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.OwnsOne(p => p.Cedula, c => c.Property(p => p.Valor).HasColumnName("Cedula"));
            entity.OwnsOne(p => p.Nombre);
            entity.OwnsOne(p => p.Contacto);
            entity.OwnsOne(p => p.Direccion);
        });

        modelBuilder.Entity<Expediente>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.NumeroExpediente).IsUnique();
        });

        modelBuilder.Entity<EvaluacionCIF>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Ignore(e => e.Codificaciones);
        });

        modelBuilder.Entity<Carnet>(entity =>
        {
            entity.HasKey(c => c.Id);
        });
    }
}