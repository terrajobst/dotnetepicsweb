const path = require("path");
const glob = require("glob-all");
const CompressionPlugin = require("compression-webpack-plugin");
const zopfli = require("@gfx/zopfli");
const TerserJsPlugin = require("terser-webpack-plugin");
const MiniCssExtractPlugin = require("mini-css-extract-plugin");
const CssMinimizerPlugin = require("css-minimizer-webpack-plugin");
const RemovePlugin = require("remove-files-webpack-plugin");
const PurgecssPlugin = require("purgecss-webpack-plugin");

var config = {
    entry: {
        js: "./wwwroot/js/index.js",
        css: "./wwwroot/css/index.js",
        css_dark: "./wwwroot/css/index-dark.js"
    },
    optimization: {
        minimize: true,
        minimizer: [new TerserJsPlugin({}), new CssMinimizerPlugin({ })]
    },
    module: {
        rules: [
            {
                test: /\.css$/,
                use: [
                    MiniCssExtractPlugin.loader,
                    {
                        loader: "css-loader",
                        options: { url: false }
                    }
                ]
            }
        ]
    },
    output: {
        filename: (pathData) => {
            if (pathData.runtime == "js")
                return "js/themesof.net.bundle.js";
            else if (pathData.runtime == "css")
                return `css/css.js`;
            else
                return `css/css-dark.js`;
        },
        path: path.resolve(__dirname, "wwwroot/")
    },
    plugins: [
        new MiniCssExtractPlugin({
            filename: (pathData) => {
                if (pathData.chunk.runtime == "css")
                    return "css/themesof.net.bundle.css";
                else
                    return "css/themesof.net.bundle-dark.css";
            }
        }),
        new PurgecssPlugin({
            paths: glob.sync([
                "**/*.razor",
                "**/*.cshtml"
            ])
        }),
        new CssMinimizerPlugin({ minimizerOptions: { preset: ["default", { discardComments: { removeAll: true } }] } }),
        new CompressionPlugin({
            compressionOptions: {
                numiterations: 15,
                level: 9
            },
            threshold: 0,
            minRatio: 2,
            algorithm(input, compressionOptions, callback) {
                return zopfli.gzip(input, compressionOptions, callback);
            },
            deleteOriginalAssets: false
        }),
        new CompressionPlugin({
            filename: "[file].br[query]",
            algorithm: "brotliCompress",
            test: /\.(js|css|html|svg)$/,
            compressionOptions: { level: 11 },
            threshold: 0,
            minRatio: 2,
            deleteOriginalAssets: false
        }),
        new RemovePlugin({
            before: {
            },
            after: {
                root: path.resolve(__dirname, "wwwroot/"),
                include: [
                    "css/css.js", "css/css.js.gz", "css/css.js.br",
                    "css/css-dark.js", "css/css-dark.js.gz", "css/css-dark.js.br"
                ]
            }
        })
    ],
    stats: {
        assets: true,
        warnings: true,
        errors: true,
        performance: true,
        optimizationBailout: true
    }
}

module.exports = (env, argv) => {
    if (argv.mode === "development") {
        //config.devtool = 'inline-sourcemap';
    }

    if (argv.mode === "production") {
        //...
    }

    return config;
};