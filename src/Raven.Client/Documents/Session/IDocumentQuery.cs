//-----------------------------------------------------------------------
// <copyright file="IDocumentQuery.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Raven.Client.Documents.Commands;
using Raven.Client.Documents.Queries;
using Raven.Client.Documents.Queries.Facets;
using Raven.Client.Documents.Queries.Spatial;

namespace Raven.Client.Documents.Session
{
    public interface IDocumentQueryBase<T>
    {
        /// <summary>
        ///     Register the query as a lazy-count query in the session and return a lazy
        ///     instance that will evaluate the query only when needed.
        /// </summary>
        Lazy<int> CountLazily();

        /// <summary>
        ///     Returns first element or throws if sequence is empty.
        /// </summary>
        T First();

        /// <summary>
        ///     Returns first element or default value for type if sequence is empty.
        /// </summary>
        T FirstOrDefault();

        /// <summary>
        ///     Returns first element or throws if sequence is empty or contains more than one element.
        /// </summary>
        T Single();

        /// <summary>
        ///     Returns first element or default value for given type if sequence is empty. Throws if sequence contains more than
        ///     one element.
        /// </summary>
        T SingleOrDefault();

        /// <summary>
        /// Gets the total count of records for this query
        /// </summary>
        /// <returns></returns>
        int Count();

        /// <summary>
        ///     Register the query as a lazy query in the session and return a lazy
        ///     instance that will evaluate the query only when needed
        /// </summary>
        Lazy<IEnumerable<T>> Lazily();

        /// <summary>
        ///     Register the query as a lazy query in the session and return a lazy
        ///     instance that will evaluate the query only when needed.
        ///     Also provide a function to execute when the value is evaluated
        /// </summary>
        Lazy<IEnumerable<T>> Lazily(Action<IEnumerable<T>> onEval);
    }

    public interface IRawDocumentQuery<T> :
        IQueryBase<T, IRawDocumentQuery<T>>,
        IDocumentQueryBase<T>, IEnumerable<T>
    {
        /// <summary>
        /// Set the full text of the RQL query that will be sent to the server.
        /// Must be the only call that is made on this query object
        /// </summary>
        IDocumentQuery<T> RawQuery(string query);

        /// <summary>
        /// Add a named parameter to the query
        /// </summary>
        IDocumentQuery<T> AddParameter(string name, object value);
    }

    /// <summary>
    ///     A query against a Raven index
    /// </summary>
    public interface IDocumentQuery<T> : 
        IEnumerable<T>, 
        IDocumentQueryBase<T, IDocumentQuery<T>>,
        IDocumentQueryBase<T>
    {
        string IndexName { get; }

        /// <summary>
        ///     Whether we should apply distinct operation to the query on the server side
        /// </summary>
        bool IsDistinct { get; }

        /// <summary>
        ///     Gets the query result. Accessing this property for the first time will execute the query.
        /// </summary>
        QueryResult QueryResult { get; }


        /// <summary>
        ///     Selects the specified fields directly from the index if the are stored. If the field is not stored in index, value
        ///     will come from document directly.
        /// </summary>
        /// <typeparam name="TProjection">Type of the projection.</typeparam>
        /// <param name="fields">Array of fields to load.</param>
        IDocumentQuery<TProjection> SelectFields<TProjection>(params string[] fields);

        /// <summary>
        ///     Selects the specified fields directly from the index if the are stored. If the field is not stored in index, value
        ///     will come from document directly.
        /// </summary>
        /// <typeparam name="TProjection">Type of the projection.</typeparam>
        /// <param name="fields">Array of fields to load.</param>
        /// <param name="projections">Array of field projections.</param>
        IDocumentQuery<TProjection> SelectFields<TProjection>(string[] fields, string[] projections);

        /// <summary>
        ///     Selects the specified fields directly from the index if the are stored. If the field is not stored in index, value
        ///     will come from document directly.
        ///     <para>Array of fields will be taken from TProjection</para>
        /// </summary>
        /// <typeparam name="TProjection">Type of the projection from which fields will be taken.</typeparam>
        IDocumentQuery<TProjection> SelectFields<TProjection>();

        /// <summary>
        ///     Ability to use one factory to determine spatial shape that will be used in query.
        /// </summary>
        /// <param name="path">Spatial field name.</param>
        /// <param name="clause">function with spatial criteria factory</param>
        IDocumentQuery<T> Spatial(Expression<Func<T, object>> path, Func<SpatialCriteriaFactory, SpatialCriteria> clause);

        /// <summary>
        ///     Ability to use one factory to determine spatial shape that will be used in query.
        /// </summary>
        /// <param name="fieldName">Spatial field name.</param>
        /// <param name="clause">function with spatial criteria factory</param>
        IDocumentQuery<T> Spatial(string fieldName, Func<SpatialCriteriaFactory, SpatialCriteria> clause);

        /// <summary>
        /// Get the facets as per the specified facet document with the given start and pageSize
        /// </summary>
        FacetedQueryResult GetFacets(string facetSetupDoc, int start, int? pageSize);

        /// <summary>
        /// Get the facet results as per the specified facets with the given start and pageSize
        /// </summary>
        FacetedQueryResult GetFacets(List<Facet> facets, int start, int? pageSize);

        /// <summary>
        ///     Get the facets lazily as per the specified doc with the given start and pageSize
        /// </summary>
        Lazy<FacetedQueryResult> GetFacetsLazy(string facetSetupDoc, int facetStart, int? facetPageSize);

        /// <summary>
        ///     Get the facets lazily as per the specified doc with the given start and pageSize
        /// </summary>
        Lazy<FacetedQueryResult> GetFacetsLazy(List<Facet> facets, int facetStart, int? facetPageSize);



        /// <summary>
        /// Changes the return type of the query
        /// </summary>
        IDocumentQuery<TResult> OfType<TResult>();

        IGroupByDocumentQuery<T> GroupBy(string fieldName, params string[] fieldNames);

    }
}
