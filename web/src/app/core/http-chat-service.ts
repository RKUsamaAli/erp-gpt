import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, from, map, switchMap, throwError } from 'rxjs';
import { environment } from '../../environments/environment';
import { AnswerBlock, AnswerChunk, AskRequest } from '../models/chat.models';
import { ChatService } from './chat-service';

type GraphQlResponse<T> = {
  data?: T | null;
  errors?: { message: string; extensions?: { code?: string } }[];
};

type TopCustomersData = {
  topCustomers: {
    customerId: number;
    customerName: string;
    territory: string | null;
    revenue: number;
    orderCount: number;
  }[];
};

type SalesByTerritoryData = {
  salesByTerritory: {
    territory: string;
    group: string;
    revenue: number;
    orderCount: number;
  }[];
};

type TopProductsData = {
  topProducts: {
    productId: number;
    productName: string;
    productNumber: string;
    category: string | null;
    revenue: number;
    unitsSold: number;
  }[];
};

type SalesSummaryData = {
  salesSummary: {
    totalRevenue: number;
    orderCount: number;
    averageOrderValue: number;
    customerCount: number;
    from: string;
    to: string;
  };
};

const RANGE_2024 = { from: '2024-01-01', to: '2024-12-31' };

const TOP_CUSTOMERS = `
  query TopCustomers($from: LocalDate!, $to: LocalDate!, $limit: Int!) {
    topCustomers(from: $from, to: $to, limit: $limit) {
      customerId
      customerName
      territory
      revenue
      orderCount
    }
  }
`;

const SALES_BY_TERRITORY = `
  query SalesByTerritory($from: LocalDate!, $to: LocalDate!) {
    salesByTerritory(from: $from, to: $to) {
      territory
      group
      revenue
      orderCount
    }
  }
`;

const TOP_PRODUCTS = `
  query TopProducts($from: LocalDate!, $to: LocalDate!, $limit: Int!) {
    topProducts(from: $from, to: $to, limit: $limit) {
      productId
      productName
      productNumber
      category
      revenue
      unitsSold
    }
  }
`;

const SALES_SUMMARY = `
  query SalesSummary($from: LocalDate!, $to: LocalDate!) {
    salesSummary(from: $from, to: $to) {
      totalRevenue
      orderCount
      averageOrderValue
      customerCount
      from
      to
    }
  }
`;

function money(value: number): string {
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: 'USD',
    maximumFractionDigits: 0,
  }).format(value);
}

function formatNumber(value: number): string {
  return new Intl.NumberFormat('en-US').format(value);
}

function chunks(blocks: AnswerBlock[]): Observable<AnswerChunk> {
  return from(blocks).pipe(map((block): AnswerChunk => ({ kind: 'block', block })));
}

/** Calls the configured GraphQL API and maps known ERP questions into answer blocks. */
@Injectable()
export class HttpChatService implements ChatService {
  private readonly http = inject(HttpClient);
  private readonly graphqlUrl = `${environment.apiUrl}/graphql`;

  readonly suggestions: readonly string[] = [
    'Show top 5 customers by revenue in 2024',
    'Show sales by territory in 2024',
    'Show top 5 products by revenue in 2024',
  ];

  ask({ question }: AskRequest): Observable<AnswerChunk> {
    const normalized = question.trim().toLowerCase();

    if (/territor|canada|region|sales by/.test(normalized)) {
      return this.salesByTerritory();
    }

    if (/product|bike|stock|inventory/.test(normalized)) {
      return this.topProducts();
    }

    if (/customer|top|revenue/.test(normalized)) {
      return this.topCustomers();
    }

    return this.salesSummary();
  }

  private topCustomers(): Observable<AnswerChunk> {
    return this.gql<TopCustomersData>(TOP_CUSTOMERS, { ...RANGE_2024, limit: 5 }).pipe(
      switchMap(({ topCustomers }) =>
        chunks([
          { kind: 'text', text: 'Here are the top 5 customers by revenue in 2024 from the deployed API:' },
          {
            kind: 'table',
            headers: ['Customer', 'Territory', 'Revenue', 'Orders'],
            rows: topCustomers.map((row) => [
              row.customerName,
              row.territory ?? 'Unassigned',
              money(row.revenue),
              formatNumber(row.orderCount),
            ]),
          },
        ]),
      ),
    );
  }

  private salesByTerritory(): Observable<AnswerChunk> {
    return this.gql<SalesByTerritoryData>(SALES_BY_TERRITORY, RANGE_2024).pipe(
      switchMap(({ salesByTerritory }) =>
        chunks([
          { kind: 'text', text: 'Sales by territory for 2024 from the deployed API:' },
          {
            kind: 'table',
            headers: ['Territory', 'Group', 'Revenue', 'Orders'],
            rows: salesByTerritory.map((row) => [
              row.territory,
              row.group,
              money(row.revenue),
              formatNumber(row.orderCount),
            ]),
          },
        ]),
      ),
    );
  }

  private topProducts(): Observable<AnswerChunk> {
    return this.gql<TopProductsData>(TOP_PRODUCTS, { ...RANGE_2024, limit: 5 }).pipe(
      switchMap(({ topProducts }) =>
        chunks([
          { kind: 'text', text: 'Here are the top 5 products by revenue in 2024 from the deployed API:' },
          {
            kind: 'table',
            headers: ['Product', 'Product number', 'Category', 'Revenue', 'Units'],
            rows: topProducts.map((row) => [
              row.productName,
              row.productNumber,
              row.category ?? 'Unassigned',
              money(row.revenue),
              formatNumber(row.unitsSold),
            ]),
          },
        ]),
      ),
    );
  }

  private salesSummary(): Observable<AnswerChunk> {
    return this.gql<SalesSummaryData>(SALES_SUMMARY, RANGE_2024).pipe(
      switchMap(({ salesSummary }) =>
        chunks([
          { kind: 'text', text: 'I can currently test the deployed GraphQL API with sales, customers, territories, and products.' },
          {
            kind: 'list',
            items: [
              `Revenue in 2024: ${money(salesSummary.totalRevenue)}`,
              `Orders: ${formatNumber(salesSummary.orderCount)}`,
              `Customers: ${formatNumber(salesSummary.customerCount)}`,
              `Average order value: ${money(salesSummary.averageOrderValue)}`,
            ],
          },
          { kind: 'text', text: 'Try "top customers", "sales by territory", or "top products".' },
        ]),
      ),
    );
  }

  private gql<T>(query: string, variables: Record<string, unknown>): Observable<T> {
    return this.http.post<GraphQlResponse<T>>(this.graphqlUrl, { query, variables }).pipe(
      map((response) => {
        if (response.errors?.length) {
          const first = response.errors[0];
          throw new Error(first.extensions?.code ? `${first.extensions.code}: ${first.message}` : first.message);
        }

        if (!response.data) {
          throw new Error('The API returned no data.');
        }

        return response.data;
      }),
      catchError((error: unknown) => throwError(() => this.toError(error))),
    );
  }

  private toError(error: unknown): Error {
    if (error instanceof HttpErrorResponse) {
      const graphQlMessage = error.error?.errors?.[0]?.message;
      return new Error(graphQlMessage ?? `API request failed with HTTP ${error.status}.`);
    }

    return error instanceof Error ? error : new Error('API request failed.');
  }
}
