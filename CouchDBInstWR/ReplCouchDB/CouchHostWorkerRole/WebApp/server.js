var express = require('express')
  , routes = require('./routes');

var app = module.exports = express.createServer();

var host = process.argv[2];
var port = process.argv[3];
var couchUrl = process.argv[4];
var dbName = process.argv[5];

var TaskProvider = require('./taskProvider').TaskProvider;
var taskProvider = new TaskProvider(couchUrl, dbName);

var Home = require('./home');
var home = new Home(taskProvider);

// Configuration
app.configure(function(){
  app.set('views', __dirname + '/views');
  app.set('view engine', 'jade');
  app.use(express.bodyParser());
  app.use(express.methodOverride());
  app.use(app.router);
  app.use(express.static(__dirname + '/public'));
});

app.configure('development', function(){
  app.use(express.errorHandler({ dumpExceptions: true, showStack: true }));
});

app.configure('production', function(){
  app.use(express.errorHandler());
});

// Routes
app.get('/', home.showItems.bind(home));
app.get('/home', home.showItems.bind(home));
app.post('/home/newitem', home.newItem.bind(home));

app.listen(port, host);
