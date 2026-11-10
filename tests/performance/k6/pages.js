// HTTP load test of the pages as the server renders them before the Blazor circuit starts.
// The criteria come from SPEC.md, section 10: 95% of responses under 500 ms with 20 users, no errors.
//   k6 run -e BASE_URL=http://127.0.0.1:5190 -e USER_ID=<id from Regal.LoadHost> tests/performance/k6/pages.js
import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
    scenarios: {
        users: { executor: 'constant-vus', vus: 20, duration: '2m' },
    },
    thresholds: {
        http_req_duration: ['p(95)<500'],
        http_req_failed: ['rate==0'],
        checks: ['rate==1'],
    },
};

const baseUrl = __ENV.BASE_URL;
const pages = [
    '/',
    '/stock',
    '/stock-documents',
    '/invoices',
    '/products',
    '/analytics/sales',
    '/analytics/abc',
    '/analytics/slow-moving',
];

export default function () {
    const params = {
        cookies: { 'test-user': __ENV.USER_ID },
        redirects: 0,
        tags: {},
    };

    for (const page of pages) {
        params.tags.page = page;
        const response = http.get(`${baseUrl}${page}`, params);
        check(response, {
            'status 200': r => r.status === 200,
            'rendered for a signed-in user': r => r.body.includes('Wyloguj'),
        });
        sleep(0.5);
    }
}
