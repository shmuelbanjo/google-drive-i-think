// שרת מקומי להרצת הפרויקט.
//
// למה צריך אותו: הדפים משתמשים ב-ES modules ובבקשות לשרת,
// ושניהם לא עובדים כשפותחים את הקובץ ישירות (file://).
//
// הפעלה:  node serve.js
// ואז לפתוח בדפדפן:  http://localhost:5500

const http = require('http');
const fs = require('fs');
const path = require('path');

const PORT = 5500;
const root = __dirname;

const types = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.ico': 'image/x-icon'
};

http.createServer((req, res) => {
  let rel = decodeURIComponent(req.url.split('?')[0]);
  if (rel === '/') rel = '/index.html';

  const file = path.join(root, rel);

  // הגנה מפני יציאה מתיקיית הפרויקט
  if (!file.startsWith(root)) {
    res.writeHead(403, { 'Content-Type': 'text/plain; charset=utf-8' });
    res.end('403');
    return;
  }

  fs.readFile(file, (err, buf) => {
    if (err) {
      res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
      res.end('הקובץ לא נמצא: ' + rel);
      return;
    }
    res.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream' });
    res.end(buf);
  });
}).listen(PORT, () => {
  console.log('');
  console.log('  השרת רץ. פתח בדפדפן:');
  console.log('  http://localhost:' + PORT);
  console.log('');
  console.log('  לעצירה: Ctrl+C');
  console.log('');
});
