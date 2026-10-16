async function test() {
  const params = new URLSearchParams();
  params.append('query', '{service="ECommerce.Api"}');
  const res = await fetch('http://localhost:3100/loki/api/v1/query_range', {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: params.toString()
  });
  const text = await res.text();
  console.log('Status:', res.status, 'Body:', text);
}
test();
